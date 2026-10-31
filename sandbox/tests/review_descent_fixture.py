"""Summarize one native saved-state descent without complete-route credit."""
from pathlib import Path
import argparse,csv,hashlib,json,math,statistics

def review(directory):
    directory=Path(directory)
    with (directory/'native.tsv').open(encoding='utf-8-sig') as stream:
        rows=list(csv.DictReader(stream,delimiter='\t'))
    if not rows:raise ValueError('No native samples')
    def number(row,key):
        value=float(row[key])
        if not math.isfinite(value):raise ValueError('Non-finite '+key)
        return value
    times=[number(r,'ut') for r in rows]
    if any(b<=a for a,b in zip(times,times[1:])):raise ValueError('Clock repeated or moved backwards inside experiment')
    for key in ['fuel','oxidizer','originalParts','altitude','physicalError','thrustAxisError','warp','dt']:
        for row in rows:number(row,key)
    centering=[r for r in rows if r['status'].startswith('centering /')]
    settled=[r for r in centering if number(r,'ut')>=times[0]+10]
    def metrics(items):
        if not items:return None
        errors=[number(r,'physicalError') for r in items]
        return {'samples':len(items),'meanPhysicalError':statistics.mean(errors),'medianPhysicalError':statistics.median(errors),'p95PhysicalError':sorted(errors)[min(len(errors)-1,int(len(errors)*.95))],
                'meanRequestedTilt':statistics.mean(number(r,'requestedTilt') for r in items),'meanActualTilt':statistics.mean(number(r,'actualTilt') for r in items),
                'saturatedFraction':sum(max(abs(number(r,'pitch')),abs(number(r,'yaw')))>=.99 for r in items)/len(items)}
    first,last=rows[0],rows[-1]
    distance=lambda r:math.hypot(number(r,'eastError'),number(r,'northError'))
    result={'fullLogisticsRoundTrip':False,'diagnostic':True,'source':str(directory),'initialSaveSHA256':hashlib.sha256((directory/'initial.sfs').read_bytes()).hexdigest(),
            'initialLedgerSHA256':hashlib.sha256((directory/'initial.xml').read_bytes()).hexdigest(),'samples':len(rows),'durationUT':times[-1]-times[0],
            'initial':{k:first[k] for k in ['ut','phase','altitude','fuel','oxidizer','originalParts','controller','verticalSpeed','eastSpeed','northSpeed']},
            'final':{k:last[k] for k in ['ut','phase','altitude','fuel','oxidizer','originalParts','landed','verticalSpeed']},
            'minimumPadDistance':min(distance(r) for r in rows),'finalPadDistance':distance(last),'fuelUsed':number(first,'fuel')-number(last,'fuel'),
            'centering':metrics(centering),'centeringAfter10s':metrics(settled),
            'controllers':sorted({int(r['controller']) for r in rows}),
            'allOriginal29':all(int(r['originalParts'])==29 for r in rows),
            'native1x':all(number(r,'warp')==1 and 0<number(r,'dt')<=.04 for r in rows),
            'maximumHeat':max(number(r,'maxHeat') for r in rows),
            'limits':['A seeded native landing experiment, not a continuous orbital return or complete logistics route.','Controller comparisons require identical initial save hashes and reported initialization noise.','Use LandingEvidence.tsv for final stable touchdown acceptance.']}
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory');args=parser.parse_args()
    report=review(args.directory);path=Path(args.directory)/'review.json';path.write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
