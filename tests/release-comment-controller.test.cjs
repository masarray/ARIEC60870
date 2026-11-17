// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
'use strict';
const {test} = require('node:test');
const assert = require('node:assert/strict');
const {
  parseVersion, compareVersion, parseSourceVersion, acceptedCiRun, hasNativeUpdaterProof,
  recentCommand, stageMarker, chooseAction, run
} = require('../scripts/release-comment-controller.cjs');

const SHA = 'a'.repeat(40);
const now = new Date().toISOString();
const command = {id:53,body:'/release stable',created_at:now,user:{login:'masarray'}};
const stage = (s)=>stageMarker(53,SHA,s);
const ref = {commit:{sha:SHA}};
const pr = {number:250,merged_at:now,merge_commit_sha:SHA,
  base:{ref:'master'},head:{repo:{full_name:'masarray/ARIEC60870'}}};
const qualifiedJobs = [
  {name:'build-test',conclusion:'success',steps:[{
    name:'OBS-C real-WPF viewport and 1400-frame acceptance',conclusion:'success'}]},
  {name:'Native Windows independent updater FAT',conclusion:'success',steps:[{
    name:'Verify independent update worker from genuine installed previous release',conclusion:'success'}]}
];
const ci = {id:4,head_sha:SHA,head_branch:'master',event:'push',
  display_title:'CI master',status:'completed',conclusion:'success',created_at:now};
const green = {head_sha:SHA,event:'push',status:'completed',conclusion:'success'};

function mock({fat=true,runningCi=false,runningPackage=false,markers=[],codeql=true,hygiene=true,
   recent=true, latestTag='v3.6.12',version='3.6.13',commentEvent=true,extraComments=[],
    artifactFromPr=false,artifactStatus='completed',artifactDispatch='success',artifactOverflow=false,
    ciConclusion='success'}={}) {
  const calls=[];
  let comments = [recent ? command : {...command,created_at:'2025-11-16T00:00:00Z'}, ...extraComments];
  const content = path => {
    const txt = path === 'Directory.Build.props' ? `<Project><Version>${version}</Version></Project>` : '# release notes';
    return {data:{type:'file',encoding:'base64',content:Buffer.from(txt).toString('base64')}};
  };
  const workflowRuns = id => {
    if(id==='ci.yml') return [runningCi?{...ci,status:'in_progress',conclusion:null}:{...ci,conclusion:ciConclusion}];
    if(id==='release-package.yml') return runningPackage ?
      [{id:10,status:'in_progress'}] : [];
    if(id==='codeql.yml') return codeql?[green]:[];
    if(id==='repository-hygiene.yml') return hygiene?[green]:[];
    return [];
  };
  const github = {
    rest:{
      repos:{
        getBranch:async()=>({data:ref}),getContent:async({path})=>content(path),
        getCollaboratorPermissionLevel:async({username})=>({data:{permission:username==='masarray'?'admin':'read'}}),
        getLatestRelease:async()=>({data:{tag_name:latestTag}}),
        getCommit:async()=>({data:{files:[{filename:'.github/workflows/release-comment.yml'}]}}),
        getReleaseByTag:async()=>{throw {status:404};}
      },
      pulls:{list:async()=>({data:[pr]})},
      issues:{listComments:async()=>({data:comments})},
      git:{getRef:async()=>{throw {status:404};}},
      actions:{
        listWorkflowRuns:async({workflow_id})=>({data:{workflow_runs:workflowRuns(workflow_id)}}),
        listJobsForWorkflowRun:async({run_id})=>({data:{jobs:run_id===789 ? [{name:'release-admission',steps:[
          {name:'Dispatch one pinned next stage',conclusion:artifactDispatch}]}] :
          (fat?qualifiedJobs:qualifiedJobs.slice(0,1))}}),
        listArtifactsForRepo:async({name})=>({data:{total_count:artifactOverflow?101:markers.includes(name)?1:0,
          artifacts:markers.includes(name)?[{name,expired:false,workflow_run:{id:789}}]:[]}}),
        getWorkflowRun:async()=>({data:{
          path:artifactFromPr?'.github/workflows/ci.yml':'.github/workflows/release-comment.yml',
          head_branch:artifactFromPr?'feature/evil':'master',head_sha:SHA,event:'issue_comment',
          status:artifactStatus,conclusion:artifactStatus==='completed'?'success':null,
          repository:{full_name:'masarray/ARIEC60870'}
        }})
      }
    },
    paginate:async(fn,args)=>{
      const result = await fn(args);
      return result.data.jobs || result.data;
    }
  };
  const context = {repo:{owner:'masarray',repo:'ARIEC60870'},eventName:commentEvent?'issue_comment':'workflow_run',
    payload:commentEvent?{comment:command,issue:{number:250,pull_request:{url:'pr'}}}:
      {workflow_run:{name:'CI',head_sha:SHA,head_branch:'master',event:'push',conclusion:'success'}}};
  const core={notice:()=>{},info:()=>{},setOutput:(key,value)=>calls.push({type:'output',key,value})};
  return {github,context,core,calls};
}

test('semver refuses malformed, prerelease and leading zeros; compares by numeric components',()=>{
  assert.deepEqual(parseVersion('3.6.13'),[3,6,13]);
  assert.equal(compareVersion('3.6.13','3.6.12'),1);
  assert.equal(compareVersion('3.6.13','3.6.13'),0);
  for(const v of ['v3.6.13','3.06.13','3.6.13-rc.1','3.6.13; evil',''])
    assert.throws(()=>parseVersion(v));
  assert.equal(parseSourceVersion('<Version>3.6.13</Version>'),'3.6.13');
});
test('CI proof cannot borrow a PR-bound dispatch or a missing native FAT step',()=>{
  assert.ok(acceptedCiRun(ci,SHA));
  assert.ok(!acceptedCiRun({...ci,display_title:'CI PR #250 updater abc',event:'workflow_dispatch'},SHA));
  assert.ok(acceptedCiRun({...ci,display_title:'CI master',event:'workflow_dispatch'},SHA));
  assert.ok(hasNativeUpdaterProof(qualifiedJobs));
  assert.ok(!hasNativeUpdaterProof(qualifiedJobs.slice(0,1)));
  assert.ok(!hasNativeUpdaterProof(qualifiedJobs.map(j=>({...j,steps:[]}))));
});
test('recent exact command only; no timeout or extra shell-like content',()=>{
  assert.ok(recentCommand(command));
  assert.ok(!recentCommand({...command,body:'/release stable && run'}));
  assert.ok(!recentCommand({...command,created_at:'2024-01-01T00:00:00Z'}));
});
test('pure decision engine waits/blocks/reuses instead of piling up packages',()=>{
  const base={published:false,packageReserved:false,updaterReserved:false,activePackage:false,
    activeCi:false,completedCiError:false,qualified:true,pendingChecks:false,failedChecks:false};
  assert.equal(chooseAction(base),'package');
  assert.equal(chooseAction({...base,qualified:false}),'updater');
  assert.equal(chooseAction({...base,activePackage:true}),'wait');
  assert.equal(chooseAction({...base,activeCi:true}),'wait');
  assert.equal(chooseAction({...base,pendingChecks:true}),'wait');
  assert.equal(chooseAction({...base,completedCiError:true}),'blocked');
  assert.equal(chooseAction({...base,qualified:false,updaterReserved:true}),'await-updater');
  assert.equal(chooseAction({...base,packageReserved:true}),'already-dispatched');
  assert.equal(chooseAction({...base,published:true}),'already-published');
});
test('valid current merged PR triggers one Package dispatch pinned to master SHA',async()=>{
  const m=mock();await run(m);
  assert.equal(m.calls.find(x=>x.key==='stage')?.value,'package');
  assert.equal(m.calls.find(x=>x.key==='sha')?.value,SHA);
  assert.equal(m.calls.find(x=>x.key==='version')?.value,'3.6.13');
});
test('unqualified master dispatches only one updater CI; never Package',async()=>{
  const m=mock({fat:false});await run(m);
  assert.equal(m.calls.find(x=>x.key==='stage')?.value,'updater');
});
test('duplicate, active CI or package suppresses additional expensive dispatch',async()=>{
  for(const cfg of [{markers:[stage('package')]},{markers:[stage('updater')],fat:false},
    {runningCi:true},{runningPackage:true}]) {
    const m=mock(cfg);await run(m);
    assert.equal(m.calls.filter(x=>x.key==='stage').length,0,JSON.stringify(cfg));
  }
});
test('stale release comment and unrelated PR cannot publish',async()=>{
  const a=mock({recent:false});await assert.rejects(()=>run(a));
  const b=mock();b.context.payload.issue.number=111;
  await assert.rejects(()=>run(b),/latest merged PR/);
  assert.equal(b.calls.length,0);
});
test('a matching completed workflow_run can resume the stored source-bound request',async()=>{
  const m=mock({commentEvent:false,fat:true});await run(m);
  assert.equal(m.calls.find(x=>x.key==='stage')?.value,'package');
});

test('untrusted recent comment cannot suppress a previous valid release intent',async()=>{
  const m=mock({commentEvent:false,extraComments:[{id:66,body:'/release stable',
    created_at:now,user:{login:'untrusted-reader'}}]});
  await run(m);
  assert.equal(m.calls.find(x=>x.key==='stage')?.value,'package');
});
test('completion of older Package run wakes current intent without borrowing its SHA',async()=>{
  const m=mock({commentEvent:false});
  m.context.payload.workflow_run={name:'Package',head_sha:'b'.repeat(40),
    head_branch:'release/rc-3.6.13-rc.1',event:'push',conclusion:'failure'};
  await run(m);
  assert.equal(m.calls.find(x=>x.key==='stage')?.value,'package');
  assert.equal(m.calls.find(x=>x.key==='sha')?.value,SHA);
});

test('a forged PR-workflow artifact cannot reserve the release stage',async()=>{
  const m=mock({markers:[stage('package')],artifactFromPr:true});
  await run(m);
  assert.equal(m.calls.find(x=>x.key==='stage')?.value,'package');
});

test('a reservation is only complete after its controller dispatch step succeeded',async()=>{
  const failed=mock({markers:[stage('package')],artifactDispatch:'failure'});
  await assert.rejects(()=>run(failed),/dispatch was not proven successful/);
  assert.equal(failed.calls.filter(x=>x.key==='stage').length,0);
  const cancelled=mock({markers:[stage('updater')],fat:false,artifactDispatch:'cancelled'});
  await assert.rejects(()=>run(cancelled),/dispatch was not proven successful/);
});

test('an in-progress trusted controller lease prevents concurrent dispatch',async()=>{
  const m=mock({markers:[stage('package')],artifactStatus:'in_progress',artifactDispatch:null});
  await run(m);
  assert.equal(m.calls.filter(x=>x.key==='stage').length,0);
});

test('artifact name flooding fails closed rather than dispatching twice',async()=>{
  const m=mock({markers:[stage('package')],artifactOverflow:true});
  await assert.rejects(()=>run(m),/Too many artifacts/);
  assert.equal(m.calls.filter(x=>x.key==='stage').length,0);
});

test('cancelled or timed-out exact-source updater CI is blocked, not awaited forever',async()=>{
  for(const conclusion of ['cancelled','timed_out','action_required','startup_failure']) {
    const m=mock({fat:false,markers:[stage('updater')],ciConclusion:conclusion});
    await assert.rejects(()=>run(m),/Release blocked/);
    assert.equal(m.calls.filter(x=>x.key==='stage').length,0,conclusion);
  }
});
