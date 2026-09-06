# /// script
# dependencies = ["pillow", "numpy"]
# ///
"""Assemble baked frame zero and deterministic placeholder art into padded atlases."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]

def bounds(mask):
    y,x=np.where(mask)
    if not len(x): raise RuntimeError('UNKNOWN: no facial feature in bake')
    return (int(x.min()),int(y.min()),int(x.max()),int(y.max()))

def bake(path):
    image=Image.open(path).convert('RGBA')
    if image.size!=(128,128): raise ValueError('Bake must use the full 128x128 UV domain')
    pixels=np.asarray(image)
    if np.any(pixels[4:124,4:124,3]!=255): raise RuntimeError('Incomplete opaque bake')
    return image

def skin_color(image):
    return tuple(np.median(np.asarray(image)[:16,:,:3],axis=(0,1)).astype(int))+(255,)

def eye_frames(base):
    data=np.asarray(base);lum=data[:,:,:3]@np.array([.2126,.7152,.0722])
    mask=lum<90;mask[105:]=False
    boxes=[]
    for lo,hi in [(0,64),(64,128)]:
        part=mask.copy();part[:,:lo]=False;part[:,hi:]=False
        boxes.append(bounds(part))
    blank=base.copy();draw=ImageDraw.Draw(blank);skin=skin_color(base)
    for x0,y0,x1,y1 in boxes: draw.rectangle((max(0,x0-4),max(0,y0-4),min(127,x1+4),min(127,y1+4)),fill=skin)
    frames=[base]
    ink=(48,30,28,255)
    for frame in range(1,6):
        image=blank.copy();draw=ImageDraw.Draw(image)
        for x0,y0,x1,y1 in boxes:
            width=x1-x0;height=y1-y0;mid=(y0+y1)/2
            if frame in (1,5):
                curve=[]
                for step in range(33):
                    t=step/32
                    y=mid+(-1 if frame==1 else 1)*height*.18*(1-(2*t-1)**2)
                    curve.append((x0+width*t,y))
                draw.line(curve,fill=ink,width=4)
            else:
                draw.ellipse((x0,y0,x1,y1),fill=ink)
                if frame==2:
                    draw.ellipse((x0+2,y0+3,x1-2,y1-3),fill=(251,238,229,255))
                    draw.ellipse((x0+width*.3,y0+height*.28,x1-width*.3,y1-height*.28),fill=ink)
                else:
                    lid=y0+height*(.60 if frame==3 else .45)
                    draw.rectangle((x0-1,y0-1,x1+1,lid),fill=skin)
                    draw.line((x0,lid,x1,lid),fill=ink,width=3)
        frames.append(image)
    frames.extend([base.copy(),base.copy()])
    return frames,mask,boxes

def mouth_frames(base):
    data=np.asarray(base);lum=data[:,:,:3]@np.array([.2126,.7152,.0722])
    mask=(lum<100)|((data[:,:,0]>data[:,:,1]*1.2)&(data[:,:,1]<145)); mask[:15]=False
    x0,y0,x1,y1=bounds(mask)
    blank=base.copy();draw=ImageDraw.Draw(blank);skin=skin_color(base)
    draw.rectangle((max(0,x0-4),max(0,y0-4),min(127,x1+4),min(127,y1+4)),fill=skin)
    cx=(x0+x1)/2;cy=(y0+y1)/2
    closed=blank.copy()
    curve=[(x0+(x1-x0)*t/32, cy+4*(1-(2*t/32-1)**2)) for t in range(33)]
    ImageDraw.Draw(closed).line(curve,fill=(95,48,48,255),width=3)
    frames=[closed]
    for width,height in [((x1-x0)*.7,(y1-y0)*.5),((x1-x0)*1.05,(y1-y0)*.95)]:
        image=blank.copy();ImageDraw.Draw(image).ellipse((cx-width/2,cy-height/2,cx+width/2,cy+height/2),fill=(115,40,53,255));frames.append(image)
    return frames,mask,[(x0,y0,x1,y1)]

def assemble(name,frames,cols,rows,mask,output):
    atlas=Image.new('RGBA',(cols*128,rows*128))
    for frame,image in enumerate(frames):
        # Bake at final UV resolution, retaining its interior pixels without rescaling.
        # Only the exterior four pixels are replaced with repeated edge texels.
        core=np.asarray(image)[4:124,4:124].copy()
        core[:,:,3]=255
        padded=np.pad(core,((4,4),(4,4),(0,0)),mode='edge')
        if np.any(padded[mask,3]!=255): raise RuntimeError('Pupil/feature transparency')
        atlas.paste(Image.fromarray(padded),(frame%cols*128,frame//cols*128))
    path=output/(name+'.png');atlas.save(path)
    # Verify the saved PNG itself, including all 4-pixel borders.
    actual=np.asarray(Image.open(path))
    for frame in range(len(frames)):
        x=frame%cols*128;y=frame//cols*128;tile=actual[y:y+128,x:x+128]
        expected=np.pad(tile[4:124,4:124],((4,4),(4,4),(0,0)),mode='edge')
        if not np.array_equal(tile,expected): raise RuntimeError('Invalid atlas padding')
        if np.any(tile[mask,3]!=255): raise RuntimeError('Nonopaque feature after save')
    if name=='FaceMouth':
        dark=[]
        for frame in range(3):
            tile=actual[15:124,frame*128:(frame+1)*128,:3]
            dark.append(int(((tile@np.array([.2126,.7152,.0722]))<100).sum()))
        if not (0 < dark[0] < dark[1]/3 and dark[1] < dark[2]):
            raise RuntimeError(f'Closed mouth was not reduced to a line: {dark}')
        print(f'MOUTH dark_px closed/half/open={dark}',flush=True)
    print(f'ATLAS {name} {atlas.width}x{atlas.height} frames={len(frames)} core=120 padding=4 transparent_feature_px=0 padding_mismatches=0',flush=True)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--bake-dir',type=Path,default=ROOT/'Logs/face-validation/baked');parser.add_argument('--output',type=Path,default=ROOT/'Assets/Characters');args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    report={}
    for name,builder,cols,rows in [('FaceEyes',eye_frames,4,2),('FaceMouth',mouth_frames,3,1)]:
        base=bake(args.bake_dir/(name+'-frame0.png'));frames,mask,boxes=builder(base)
        assemble(name,frames,cols,rows,mask,args.output)
        report[name]={'feature_boxes':boxes,'baked_frame':0 if name=='FaceEyes' else None,'bake_derived_frames':[0] if name=='FaceMouth' else [],'placeholder_frames':list(range(1,6)) if name=='FaceEyes' else [0,1,2], 'reserved_frames':[6,7] if name=='FaceEyes' else []}
    (args.bake_dir/'atlas-report.json').write_text(json.dumps(report,indent=2)+'\n')

if __name__=='__main__':main()
