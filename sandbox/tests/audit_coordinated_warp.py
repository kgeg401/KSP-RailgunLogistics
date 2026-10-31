"""Separate read-only proof of native coordinated coasts; never awards full trips.

The existing strict 1x round-trip audit is deliberately unchanged. This reader
requires both native traces, including real wall timestamps and actual mode.
"""
from pathlib import Path
import argparse,csv,json,math,re
from audit_roundtrip import private_endpoint

CHEATS=('infinitePropellant','infiniteElectricity','ignoreTemperature','unbreakableJoints','noCrashDamage')
MAX_GAP=2.0

def number(row,key):
    value=float(row[key])
    if not math.isfinite(value):raise ValueError('Nonfinite '+key)
    return value

def wall(row):return int(row['utcTicks'])/10000000
def key(row):return (row['epoch'],row['session']) if row['epoch'] and row['session'] else None

def safe(row):
    if number(row,'atmosphere')<0:return False
    if number(row,'thrustKN')>.05 or abs(number(row,'throttle'))>.001:return False
    if any(abs(number(row,k))>.05 for k in ('x','y','z')):return False
    if row['landed']=='True':return 0<=number(row,'surfaceSpeed')<.1
    return (row['landed']=='False' and row['phase'] in ('Rendezvous','Landing') and
            number(row,'altitude')>number(row,'atmosphere')+30000 and
            number(row,'periapsis')>number(row,'atmosphere')+10000)

def review(traces,expected=('RG-Test-A','RG-Test-B')):
    result={'success':False,'coordinatedNativeWarpEvidenceVerified':False,'fullLogisticsRoundTrip':False,'checks':{},'sessions':[]}
    checks=result['checks']
    try:
        assert len(traces)==2 and len(expected)==2 and len(set(expected))==2,'Exactly two native peers required'
        players={}
        groups={}
        for role,rows in traces.items():
            assert len(rows)>=4,'Missing native peer samples'
            names={x['player'] for x in rows};assert len(names)==1,'Native peer identity changed'
            player=next(iter(names));assert player in expected and player not in players,'Unexpected or duplicate native peer'
            players[player]=rows
            previous=None
            for index,row in enumerate(rows):
                real=number(row,'real');ut=number(row,'ut');rate=number(row,'warp')
                assert 1<=rate<=100,'Warp outside the installed native cap'
                assert row['stage'] in ('Idle','Preparing','Warping','Synchronizing'),'Unknown coordinator stage'
                assert all(row[k]=='False' for k in CHEATS),'Native cheat flag enabled or missing'
                if rate==1:assert abs(number(row,'dt')-.02)<1e-7,'Nonstandard 1x physics timestep'
                if previous:
                    assert real>number(previous,'real') and wall(row)>=wall(previous),'Native wall clock is not forward'
                    assert ut>=number(previous,'ut')-1e-6,'Unexplained native rewind'
                    if row['holding']=='True' or previous['holding']=='True':
                        assert real-number(previous,'real')<=MAX_GAP,'Missing coast evidence interval'
                session=key(row)
                if session and row['stage']!='Idle':
                    assert row['enabled']==row['holding']=='True','Missing actual native consent or hold'
                    assert len(row['members'].split(';'))==2 and set(row['members'].split(';'))==set(expected),'Missing authenticated session member'
                    assert row['leader'] in expected and row['pod']=='1','Unexpected pilot or pod'
                    assert safe(row),'Powered or unsafe coordinated coast'
                    assert number(row,'stopUt')>0 and int(row['sourceSubspace'])>=0,'Missing native coast boundary'
                    groups.setdefault(session,{}).setdefault(player,[]).append((index,row))
                if rate>1:
                    assert session and row['stage']=='Warping' and row['player']==row['leader'],'Warp without the native leader session'
                    assert row['warpMode']=='HIGH','Coast must use actual native rails warp'
                    assert row['nodeTriggered']==row['deorbitTriggered']=='False','Warp continued into a triggered burn'
                    assert safe(row),'Warp applied during unsafe or powered flight'
                    allowance=100*((real-number(previous,'real')) if previous else .1)+.02
                    assert ut<=number(row,'stopUt')+allowance,'Native warp exceeded its bounded stop'
                previous=row
        checks['twoAuthenticatedNativePeers']=set(players)==set(expected)
        assert checks['twoAuthenticatedNativePeers'],'One peer is absent'
        verified={}
        for session,peers in groups.items():
            accelerated=[row for values in peers.values() for _,row in values if number(row,'warp')>1]
            if not accelerated:continue # Canceled acknowledgements are not actual-warp proof.
            assert set(peers)==set(expected),'Accelerated session missing from one native peer'
            records=[row for values in peers.values() for _,row in values]
            leaders={x['leader'] for x in records};sources={x['sourceSubspace'] for x in records};stops={x['stopUt'] for x in records}
            assert len(leaders)==len(sources)==len(stops)==1,'Session boundaries or pilot disagree'
            destinations={int(x['destination']) for x in records if int(x['destination'])>=0}
            assert len(destinations)==1,'Missing or inconsistent native destination'
            destination=next(iter(destinations));assert str(destination) not in sources,'Accelerated coast did not establish a native destination'
            endings={}
            for player,values in peers.items():
                stages={x['stage'] for _,x in values}
                assert {'Warping','Synchronizing'}<=stages,'Missing native warp/synchronization progression'
                warping=[x for _,x in values if x['stage']=='Warping']
                assert number(warping[-1],'real')-number(warping[0],'real')<=7+MAX_GAP,'Native coast session exceeded its declared real-time bound'
                following=next((x for x in players[player][values[-1][0]+1:] if x['stage']=='Idle'),None)
                assert following and int(following['nativeSubspace'])==destination and number(following,'warp')==1,'Guidance destination not observed at 1x'
                endings[player]=following
            ea,eb=endings.values()
            assert abs((number(ea,'ut')-number(eb,'ut'))-(wall(ea)-wall(eb)))<=6,'Peers did not resynchronize their native clocks'
            verified[session]={'leader':next(iter(leaders)),'destination':destination,'peers':peers}
            result['sessions'].append({'epoch':session[0],'session':session[1],'leader':next(iter(leaders)),
                'destination':destination,'acceleratedSamples':len(accelerated)})
        assert verified,'No completed actual native warp session observed'
        # Every large clock advance needs measured native acceleration or a
        # destination synchronization bounded by the OTHER peer's actual clock.
        for player,rows in players.items():
            for a,b in zip(rows,rows[1:]):
                delta=number(b,'ut')-number(a,'ut')
                if delta<=6:continue
                session=key(b) or key(a);assert session in verified,'Unexplained native clock jump'
                group=verified[session]
                if player==group['leader'] and (number(a,'warp')>1 or number(b,'warp')>1):
                    assert delta<=100*(number(b,'real')-number(a,'real')+.1)+.02,'Native advance exceeds actual rate and real elapsed time'
                else:
                    assert a['stage']=='Synchronizing' or b['stage']=='Synchronizing','Clock advanced outside native resynchronization'
                    assert int(b['nativeSubspace'])==group['destination'],'Clock advanced onto an unrelated timeline'
                    other=players[next(name for name in expected if name!=player)]
                    candidates=[x for x in other if abs(wall(x)-wall(b))<=MAX_GAP]
                    assert candidates,'Missing counterpart clock near native sync'
                    nearest=min(candidates,key=lambda x:abs(wall(x)-wall(b)))
                    assert abs((number(b,'ut')-number(nearest,'ut'))-(wall(b)-wall(nearest)))<=6,'Native sync jump has no matching pilot clock'
        checks['boundedNativeSessionsAndAllClockJumps']=True
        checks['poweredAndAtmosphericStagesRemainOneX']=all(number(x,'warp')==1 or safe(x) for rows in players.values() for x in rows)
        result['success']=result['coordinatedNativeWarpEvidenceVerified']=all(checks.values())
        result['limits']=['Warp-session evidence only: does not establish launch, delivery, landing, refit or a full logistics trip.',
                          'Strict 1x round-trip acceptance is separate and unchanged. Missing evidence fails this review.']
    except (AssertionError,KeyError,TypeError,ValueError,IndexError) as error:result['error']=str(error)
    return result

def audit(directory):
    d=Path(directory)
    try:
        manifest=json.loads((d/'manifest.json').read_text(encoding='utf-8-sig'))
        assert manifest.get('coastTraceSchema')=='native-coast-v2','Missing native telemetry schema/provenance'
        assert private_endpoint(d,manifest),'Actual private endpoint was not preserved'
        for field in ('clientSHA256','probeSHA256'):
            hashes=manifest.get(field,{})
            assert set(hashes)=={'A','B'} and len(set(hashes.values()))==1 and all(re.fullmatch('[0-9a-f]{64}',value) for value in hashes.values()),'Unpinned or mismatched native build'
        pids=manifest.get('processIds',{})
        assert len(pids)==3 and len(set(pids.values()))==3,'Missing native process identities'
        traces={}
        for role in ('A','B'):
            with (d/('client-'+role)/'roundtrip/coast-warp.tsv').open(encoding='utf-8-sig') as stream:
                traces[role]=list(csv.DictReader(stream,delimiter='\t'))
        result=review(traces)
        result['nativeBuildsPinned']=True
        return result
    except (OSError,UnicodeError,AssertionError,ValueError,TypeError,KeyError,AttributeError,csv.Error) as error:return {'success':False,'coordinatedNativeWarpEvidenceVerified':False,'fullLogisticsRoundTrip':False,'error':str(error)}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory');args=parser.parse_args();value=audit(args.directory)
    (Path(args.directory)/'coordinated-warp-audit.json').write_text(json.dumps(value,indent=2));print(json.dumps(value,indent=2));raise SystemExit(0 if value['success'] else 1)
