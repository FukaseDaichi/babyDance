# /// script
# dependencies = ["pillow", "numpy"]
# ///
"""Render imported FBX, measure pupil coverage/interior occlusion, and compare all humanoid bones."""
import argparse
import csv
import json
from pathlib import Path
import subprocess
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[1]

def pixels(path):
    with Image.open(path) as image:
        if image.size != (1024,1024): raise ValueError(f'Unexpected render size: {path}')
        return np.asarray(image.convert('RGB'),dtype=np.int16)

def measure(folder):
    records=[]
    for angle in [0,-30,30,-60,60]:
        baseline=pixels(folder/f'{angle}_baseline.png')
        covered=pixels(folder/f'{angle}_covered.png')
        region=pixels(folder/f'{angle}_eye-region.png')[:,:,0]>250
        silhouette=pixels(folder/f'{angle}_silhouette.png')[:,:,0]>250
        lum=lambda p: p[:,:,0]*0.2126+p[:,:,1]*0.7152+p[:,:,2]*0.0722
        pupils=region & (lum(baseline)<60)
        residual=pupils & (lum(covered)<60)
        interior=np.asarray(Image.fromarray(silhouette).filter(ImageFilter.MinFilter(9)))
        # Counting EVERY non-decal pixel is stronger than choosing a guessed hair-colour threshold.
        magenta=(covered[:,:,0]>250)&(covered[:,:,1]<5)&(covered[:,:,2]>250)
        intrusion=interior & ~magenta
        record=dict(angle=angle,baseline_pupil_px=int(pupils.sum()),remaining_pupil_px=int(residual.sum()),
                    silhouette_px=int(silhouette.sum()),interior_px=int(interior.sum()),interior_non_decal_px=int(intrusion.sum()))
        record['hair_px_upper_bound']=record['interior_non_decal_px']
        records.append(record)
        Image.fromarray(np.uint8(pupils)*255).save(folder/f'{angle}_pupil-mask.png')
        Image.fromarray(np.uint8(residual)*255).save(folder/f'{angle}_remaining-mask.png')
        print(json.dumps(record),flush=True)
    if records[0]['baseline_pupil_px']==0: raise RuntimeError('UNKNOWN: front baseline has no detected pupils')
    if any(r['interior_px']==0 for r in records): raise RuntimeError('UNKNOWN: empty decal interior')
    return records

def compare_rig(before,after):
    def read(path):
        with path.open() as f: rows=list(csv.DictReader(f))
        result={(r['clip'],r['bone']):r for r in rows}
        if not rows or len(result)!=len(rows): raise RuntimeError('UNKNOWN: empty or duplicate rig rows')
        return result
    a,b=read(before),read(after)
    if a.keys()!=b.keys(): raise RuntimeError('Rig bone/clip set changed')
    differences=[];maximum=0.;present=0
    for key in a:
        if a[key]['present']!=b[key]['present']: raise RuntimeError(f'Rig mapping changed: {key}')
        if a[key]['present']!='True': continue
        present+=1
        for field in ['minBoneY','maxBoneY']:
            delta=abs(float(a[key][field])-float(b[key][field]));maximum=max(maximum,delta)
            if delta!=0: differences.append(dict(clip=key[0],bone=key[1],field=field,before=a[key][field],after=b[key][field],delta=delta))
    if present==0: raise RuntimeError('UNKNOWN: no humanoid bones sampled')
    report=dict(rows=len(a),present_rows=present,max_abs_delta=maximum,differences=differences)
    print(f'RIG rows={len(a)} present={present} max_abs_delta={maximum}',flush=True)
    return report

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--input',type=Path,default=ROOT/'Assets/Characters/BabyBunny.fbx')
    parser.add_argument('--render-dir',type=Path,default=ROOT/'Logs/face-validation/render-imported')
    parser.add_argument('--measure-only',action='store_true')
    parser.add_argument('--rig-before',type=Path)
    parser.add_argument('--rig-after',type=Path)
    args=parser.parse_args();folder=args.render_dir.resolve();folder.mkdir(parents=True,exist_ok=True)
    if not args.measure_only:
        with (folder/'blender.log').open('w') as log:
            result=subprocess.run(['/opt/homebrew/bin/blender','--background','--factory-startup','--python',str(ROOT/'tools/build_face_decals.py'),'--','--verify-existing','--input',str(args.input.resolve()),'--render-dir',str(folder)],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
        if result.returncode!=0 or 'FACE_DECALS_DONE' not in (folder/'blender.log').read_text():
            raise RuntimeError('Blender did not finish: '+str(folder/'blender.log'))
    records=measure(folder)
    report={'angles':records}
    if bool(args.rig_before)!=bool(args.rig_after): raise ValueError('Both rig snapshots are required')
    if args.rig_before: report['rig']=compare_rig(args.rig_before,args.rig_after)
    (folder/'measurements.json').write_text(json.dumps(report,indent=2)+'\n')
    if any(r['remaining_pupil_px'] or r['interior_non_decal_px'] for r in records):
        raise SystemExit('FAIL: pupil coverage or decal interior occlusion')
    if report.get('rig',{}).get('differences'): raise SystemExit('FAIL: rig bounds changed')
    print('PASS: all measured coverage/interior checks'+(' and rig equality' if args.rig_before else '; rig NOT MEASURED'))

if __name__=='__main__': main()
