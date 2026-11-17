// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
'use strict';

// Trusted default-branch release control plane. No checkout of PR source with a write token.
// The issue comment is the durable release intent; stage markers make retries idempotent.
const COMMAND = '/release stable';
const MARKER = 'release-stage-v1';
const ACTIVE = new Set(['queued', 'in_progress', 'waiting', 'pending', 'requested']);
const MAX_INTENT_AGE_MS = 24 * 60 * 60 * 1000;

function parseVersion(value) {
  const m = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(value || '');
  if (!m) throw new Error('Stable version must be plain SemVer X.Y.Z.');
  const parts = m.slice(1).map(Number);
  if (parts.some(n => !Number.isSafeInteger(n))) throw new Error('Version component exceeds safe integer range.');
  return parts;
}
function compareVersion(a, b) {
  const x = parseVersion(a), y = parseVersion(b);
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return Math.sign(x[i] - y[i]);
  return 0;
}
function parseSourceVersion(xml) {
  const match = /<Version>([^<]+)<\/Version>/.exec(xml);
  if (!match) throw new Error('Directory.Build.props has no Version.');
  parseVersion(match[1]);
  return match[1];
}
function acceptedCiRun(run, sha) {
  return run.head_sha === sha &&
    (run.event === 'push' && run.head_branch === 'master' ||
     run.event === 'workflow_dispatch' && run.display_title === 'CI master');
}
function hasNativeUpdaterProof(jobs) {
  const build = jobs.find(j => j.name === 'build-test' && j.conclusion === 'success');
  const fat = jobs.find(j => j.name === 'Native Windows independent updater FAT' && j.conclusion === 'success');
  return !!build && !!fat && (build.steps || []).some(s =>
    s.name === 'OBS-C real-WPF viewport and 1400-frame acceptance' && s.conclusion === 'success') &&
    (fat.steps || []).some(s =>
      s.name === 'Verify independent update worker from genuine installed previous release' && s.conclusion === 'success');
}
function recentCommand(comment, now = Date.now()) {
  return comment?.body?.trim() === COMMAND &&
    Number.isFinite(Date.parse(comment.created_at)) &&
    now - Date.parse(comment.created_at) >= 0 &&
    now - Date.parse(comment.created_at) < MAX_INTENT_AGE_MS;
}
function stageMarker(requestId, sha, stage) {
  return `${MARKER}-${requestId}-${sha.slice(0,12)}-${stage}`;
}
function chooseAction({ published, packageReserved, updaterReserved, activePackage,
  activeCi, completedCiError, qualified, pendingChecks, failedChecks }) {
  if (published) return 'already-published';
  if (packageReserved) return 'already-dispatched';
  if (failedChecks || completedCiError) return 'blocked';
  if (pendingChecks || activePackage || activeCi) return 'wait';
  if (qualified) return 'package';
  return updaterReserved ? 'await-updater' : 'updater';
}

async function listRuns(github, owner, repo, workflow_id) {
  const { data } = await github.rest.actions.listWorkflowRuns({owner, repo, workflow_id, per_page: 100});
  return data.workflow_runs || [];
}
async function findLatestMergedPr(github, owner, repo, sha) {
  const { data } = await github.rest.pulls.list({owner, repo, state:'closed', base:'master',
    sort:'updated', direction:'desc', per_page: 50});
  return data.find(p => p.merged_at && p.merge_commit_sha === sha &&
    p.base.ref === 'master' && p.head.repo?.full_name === `${owner}/${repo}`);
}
async function authorized(github, owner, repo, username) {
  if (!username || username.endsWith('[bot]')) return false;
  const { data } = await github.rest.repos.getCollaboratorPermissionLevel({owner,repo,username});
  return ['admin', 'maintain', 'write'].includes(data.permission);
}
async function getContent(github, owner, repo, path, ref) {
  const { data } = await github.rest.repos.getContent({owner, repo, path, ref});
  if (!data || Array.isArray(data) || data.type !== 'file' || data.encoding !== 'base64')
    throw new Error(`Expected a file: ${path}`);
  return Buffer.from(data.content.replace(/\s/g,''), 'base64').toString('utf8');
}
async function findIntent(github, context, sha, pr, owner, repo) {
  const comments = await github.paginate(github.rest.issues.listComments, {
    owner, repo, issue_number: pr.number, per_page: 100
  });
  let request;
  if (context.eventName === 'issue_comment') {
    request = comments.find(c => c.id === context.payload.comment.id);
    if (!request || !recentCommand(request)) throw new Error('Release request not found or expired.');
    if (!await authorized(github, owner, repo, request.user?.login))
      throw new Error('Only write-access maintainers may publish.');
  } else {
    // A public reader cannot replace or poison an earlier authorized intent.
    const permissionCache = new Map();
    for (const candidate of comments.slice().reverse()) {
      if (!recentCommand(candidate)) continue;
      const login = candidate.user?.login || '';
      if (!permissionCache.has(login))
        permissionCache.set(login, await authorized(github, owner, repo, login));
      if (permissionCache.get(login)) { request = candidate; break; }
    }
    if (!request) return null;
  }
  return {request, comments};
}
// A reservation is *not* proof of dispatch: upload-artifact precedes dispatch.
// Trust only the default-branch controller's own artifact AND completed dispatch step.
// A failed/cancelled controller cannot silently turn a reservation into "done".
// Failed dispatch is fail-closed; a fresh authorized comment creates a new request ID.
async function reserved(github, owner, repo, name, sha) {
  const {data} = await github.rest.actions.listArtifactsForRepo({owner,repo,name,per_page:100});
  // Never lose a valid marker among attacker-supplied, same-name artifacts.
  // Bounded search; overflow blocks release rather than risking duplicate dispatch.
  if (data.total_count > 100)
    throw new Error(`Too many artifacts named ${name}; release admission needs investigation.`);
  for (const artifact of (data.artifacts || []).filter(a =>
    !a.expired && a.name === name && a.workflow_run?.id)) {
    const runId = artifact.workflow_run.id;
    const {data: run} = await github.rest.actions.getWorkflowRun({owner,repo,run_id:runId});
    const path = String(run.path || '').split('@')[0];
    if (path !== '.github/workflows/release-comment.yml' ||
        run.repository?.full_name !== `${owner}/${repo}` ||
        run.head_branch !== 'master' || run.head_sha !== sha ||
        !['issue_comment','workflow_run'].includes(run.event)) continue;
    // A concurrent controller that has uploaded the lease is still the owner.
    if (run.status !== 'completed') return true;
    const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun,
      {owner,repo,run_id:runId,per_page:100});
    const dispatchOk = jobs.some(j => j.name === 'release-admission' &&
      (j.steps || []).some(s => s.name === 'Dispatch one pinned next stage' &&
        s.conclusion === 'success'));
    if (run.conclusion === 'success' && dispatchOk) return true;
    throw new Error(`Release reservation ${name} belongs to controller run #${runId} but its dispatch was not proven successful. No automatic retry or duplicate package; inspect the failed run and create a fresh authorized /release stable comment after repair.`);
  }
  return false;
}

async function run({github, context, core}) {
  const {owner, repo} = context.repo;
  if (context.eventName === 'issue_comment' && context.payload.comment?.body?.trim() !== COMMAND) return;
  if (context.eventName === 'workflow_run') {
    const completed = context.payload.workflow_run;
    const packageFinished = completed.name === 'Package';
    if (!packageFinished &&
        (completed.head_branch !== 'master' || completed.conclusion !== 'success' ||
         !['push','workflow_dispatch'].includes(completed.event) ||
         completed.display_title?.startsWith('CI PR #'))) return;
  }
  const branch = (await github.rest.repos.getBranch({owner,repo,branch:'master'})).data;
  const sha = branch.commit.sha;
  // Completion of an older Package run frees the global lock, but it is
  // NEVER test or source evidence for the current master SHA.
  if (context.eventName === 'workflow_run' &&
      context.payload.workflow_run.name !== 'Package' &&
      context.payload.workflow_run.head_sha !== sha) return;
  const pr = await findLatestMergedPr(github,owner,repo,sha);
  if (!pr) {
    if (context.eventName === 'issue_comment') throw new Error('No same-repository PR merged at current master HEAD.');
    return;
  }
  if (context.eventName === 'issue_comment' &&
      (!context.payload.issue?.pull_request || context.payload.issue.number !== pr.number))
    throw new Error(`Release comments must be on latest merged PR #${pr.number}, not an old PR.`);
  const intent = await findIntent(github, context, sha, pr, owner, repo);
  if (!intent) return;
  const {request, comments} = intent;
  const version = parseSourceVersion(await getContent(github, owner, repo, 'Directory.Build.props', sha));
  await getContent(github,owner,repo,`docs/archive/release-notes/RELEASE_NOTES_v${version}.md`,sha);
  const tag = `v${version}`;
  let published = false;
  try {
    const { data: release } = await github.rest.repos.getReleaseByTag({owner, repo, tag});
    if (release.draft) throw new Error(`Draft release ${tag} exists: requires explicit human reconciliation.`);
    // Compare public tag against the immutable source, rather than treating the name as enough.
    const { data: ref } = await github.rest.git.getRef({owner,repo,ref:`tags/${tag}`});
    let object = ref.object;
    for (let i=0; i<5 && object.type === 'tag'; i++)
      object = (await github.rest.git.getTag({owner,repo,tag_sha:object.sha})).data.object;
    if (object.type !== 'commit' || object.sha !== sha)
      throw new Error(`${tag} belongs to ${object.sha}; it cannot be rebound to ${sha}.`);
    published = true;
  } catch (e) { if (e.status !== 404) throw e; }
  if (published) { core.notice(`${tag} already published at exact ${sha}; no workflow started.`); return; }
  try {
    const {data:tagRef}=await github.rest.git.getRef({owner,repo,ref:`tags/${tag}`});
    if (tagRef) throw new Error(`Existing ${tag} tag without public release; refuse silent overwrite.`);
  } catch(e) { if(e.status !== 404) throw e; }
  try {
    const { data: latest } = await github.rest.repos.getLatestRelease({owner,repo});
    if (compareVersion(version, latest.tag_name.replace(/^v/,'')) <= 0)
      throw new Error(`Source ${tag} is not newer than latest stable ${latest.tag_name}.`);
  } catch(e) { if(e.status !== 404) throw e; }

  const [allCi, packages, codeql, hygiene] = await Promise.all([
    listRuns(github,owner,repo,'ci.yml'),
    listRuns(github,owner,repo,'release-package.yml'),
    listRuns(github,owner,repo,'codeql.yml'),
    listRuns(github,owner,repo,'repository-hygiene.yml')
  ]);
  const ciRuns = allCi.filter(r => acceptedCiRun(r,sha));
  const runningCi = ciRuns.some(r => ACTIVE.has(r.status));
  const succeeded = ciRuns.filter(r => r.conclusion === 'success' &&
    Date.now() - Date.parse(r.created_at) < 30*24*3600*1000);
  let qualified = false;
  for(const candidate of succeeded.slice(0,4)) {
    const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun,
      {owner,repo,run_id:candidate.id,per_page:100});
    if (hasNativeUpdaterProof(jobs)) { qualified = true; break; }
  }
  const isActive = r => ACTIVE.has(r.status);
  const exactCheck = runs => runs.filter(r => r.head_sha === sha &&
    (r.event === 'push' || r.event === 'workflow_dispatch'));
  const sec = [exactCheck(codeql), exactCheck(hygiene)];
  const {data: commit} = await github.rest.repos.getCommit({owner,repo,ref:sha});
  const expectedCodeQL = (commit.files || []).some(f =>
    /^(src|tests)\/.*\.(cs|xaml|csproj)$|^Directory\.Build\.props$|^\.github\/workflows\/codeql\.yml$/.test(f.filename));
  if (expectedCodeQL && !sec[0].some(r => r.conclusion === 'success') &&
      !sec[0].some(isActive))
    throw new Error(`Required CodeQL check missing or unsuccessful at ${sha}.`);
  const pendingChecks = sec.some(runs => runs.some(isActive));
  const failedChecks = sec.some(runs => {
    const latest = runs[0];
    return latest && latest.status === 'completed' &&
      !['success','skipped','neutral'].includes(latest.conclusion);
  });
  // Hygiene is an always-on master gate, not a green but skipped job.
  const hygienePassed = sec[1].some(r => r.conclusion === 'success');
  if (!hygienePassed && !sec[1].some(isActive))
    throw new Error(`No successful master repository hygiene check at ${sha}.`);
  // A cancelled/timed-out updater FAT is not "still awaiting" forever.
  // Never retry an ambiguous or failed installed-updater test automatically.
  const latestCi = ciRuns[0];
  const completedCiError = !qualified && latestCi?.status === 'completed' &&
    !['success', 'skipped', 'neutral'].includes(latestCi.conclusion);
  const activePackage = packages.some(isActive);
  const [packageReserved, updaterReserved] = await Promise.all([
    reserved(github,owner,repo,stageMarker(request.id,sha,'package'),sha),
    reserved(github,owner,repo,stageMarker(request.id,sha,'updater'),sha)
  ]);
  const next = chooseAction({published, packageReserved, updaterReserved,
    activePackage, activeCi:runningCi, completedCiError, qualified,
    pendingChecks, failedChecks});
  core.info(`Release intent ${tag}@${sha.slice(0,12)}, PR #${pr.number}, decision ${next}.`);
  if (next === 'already-dispatched' || next === 'already-published') return;
  if (next === 'blocked') throw new Error(`Release blocked: failed source CI or security gate on ${sha}. Investigate; no automatic retry.`);
  if (next === 'wait' || next === 'await-updater') {
    core.notice(`${tag}: ${next} (reuse existing runs, no new Windows queue).`);
    return;
  }
  // Cheap final TOCTOU guard immediately before any costly workflow dispatch.
  const latestSha = (await github.rest.repos.getBranch({owner,repo,branch:'master'})).data.commit.sha;
  if (latestSha !== sha) throw new Error('Master moved during release admission; refused stale dispatch.');
  // Actions uploads the durable stage reservation first; a later workflow
  // step dispatches the chosen stage only after the reservation is visible.
  core.setOutput('stage',next);
  core.setOutput('request_id',String(request.id));
  core.setOutput('sha',sha);
  core.setOutput('sha_short',sha.slice(0,12));
  core.setOutput('version',version);
  core.notice(next === 'updater'
    ? `Exact-SHA v${version} updater FAT missing: reserve one targeted CI run.`
    : `Exact-SHA v${version} FAT/security passed: reserve one Package run.`);
}
module.exports = {COMMAND, MARKER, parseVersion, compareVersion, parseSourceVersion,
  acceptedCiRun, hasNativeUpdaterProof, recentCommand, stageMarker, chooseAction, run};
