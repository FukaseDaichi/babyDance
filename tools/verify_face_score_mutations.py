# /// script
# dependencies = []
# ///
"""Run each FaceScore mutation through Unity, require its named failure, restore and pass."""
from pathlib import Path
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/Scripts/FaceScore.cs'
OUT = ROOT / 'Logs/face-validation'
MUTATIONS = [
    ('jitter', 'Jitter = 4.0', 'Jitter = 0.0', 'BlinkIntervalsHaveNonzeroVariance'),
    ('firing', 'if (phase >= 0 && phase < BlinkLength)', 'if (false)', 'BlinkFiresOncePerPeriodAcrossThousandBeats'),
    ('closed-width', 'Closed = 0.3', 'Closed = 0.2', 'EveryBlinkHasPointThreeBeatsOfClosedEyes'),
    ('negative-floor', '(long)Math.Floor(beat / Interval)', '(long)(beat / Interval)', 'NegativeBeatsUseFloorAndKeepMouthPhase'),
    ('atlas-row', '1.0 - (double)(row + 1) / rows', '(double)row / rows', 'AtlasRowsAreTopToBottomForEveryFrame'),
    ('forced-blink', 'phase = beat - switchBeat;', 'phase = phase + 0;', 'SwitchingForcesSharedBlinkImmediately'),
    ('composition', 'return new FacePose(eye, mouth);', 'return new FacePose(expression, mouth);', 'ExpressionsChangeOnlyOpenEyes'),
    ('mouth-range', 'f < 0.15 ? 2 : f < 0.35 ? 1 : 0', 'f < 0.15 ? 2 : f < 0.35 ? 1 : 3', 'MouthAlwaysStaysInItsThreeFrames'),
]

def run(label):
    with (OUT / (label + '.command.log')).open('w') as log:
        result = subprocess.run(['tools/unity.sh', 'test', 'EditMode', '42'], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
    logfile = ROOT / 'Logs/EditMode.log'
    grep = subprocess.run(['grep', '-E', 'error CS|Failed', str(logfile)], text=True, capture_output=True)
    if grep.returncode not in (0, 1):
        raise RuntimeError('log grep failed')
    (OUT / (label + '.grep.log')).write_text(grep.stdout)
    shutil.copy2(logfile, OUT / (label + '.log'))
    xml = ROOT / 'Logs/EditMode.xml'
    if not xml.exists():
        raise RuntimeError('No XML: Unity GUI may be open; stop')
    shutil.copy2(xml, OUT / (label + '.xml'))
    tree = ET.parse(xml).getroot()
    if tree.get('total') != '42':
        raise RuntimeError(f'Unexpected test count: {tree.attrib}')
    return result.returncode, tree

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    original = SOURCE.read_text()
    records = []
    try:
        for name, old, new, target in MUTATIONS:
            assert original.count(old) == 1, (name, 'mutation must match once')
            SOURCE.write_text(original.replace(old, new))
            code, tree = run(name + '-broken')
            failed = [x.get('name') for x in tree.iter('test-case') if x.get('result') == 'Failed']
            if code == 0 or target not in failed:
                raise RuntimeError(f'{name} did not fail target {target}: {failed}')
            print(f'{name}: BROKEN failed={tree.get("failed")} target={target}', flush=True)
            SOURCE.write_text(original)
            code, tree = run(name + '-restored')
            if code != 0 or tree.get('result') != 'Passed' or tree.get('passed') != '42':
                raise RuntimeError(f'{name} restoration failed: {tree.attrib}')
            records.append(dict(mutation=name, old=old, new=new, target=target, failed_tests=failed, restored_passed=42))
            (OUT / 'mutations.json').write_text(json.dumps(records, indent=2) + '\n')
            print(f'{name}: RESTORED 42/42 Passed', flush=True)
    finally:
        SOURCE.write_text(original)

if __name__ == '__main__':
    main()
