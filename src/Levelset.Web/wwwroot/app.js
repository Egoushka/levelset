const form = document.getElementById('check');
const statusEl = document.getElementById('status');
const result = document.getElementById('result');

async function run(url) {
  const response = await fetch('/api/v1/check?url=' + encodeURIComponent(url));
  const body = await response.json();
  if (!response.ok) throw new Error(body.error || 'The check failed');
  return body;
}

function el(tag, text, cls) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  if (cls) node.className = cls;
  return node;
}

function copyButton(label, text) {
  const b = el('button', label, 'copy');
  b.type = 'button';
  b.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(text);
      b.textContent = 'Copied';
    } catch {
      b.textContent = 'Copy failed';
    }
    setTimeout(() => { b.textContent = label; }, 2000);
  });
  return b;
}

function render(report) {
  result.replaceChildren();
  result.append(el('p', report.grade + ' ' + report.score + ' / 100', 'score'));
  const cats = Object.entries(report.categories).filter(([, v]) => v !== null).map(([k, v]) => k + ' ' + v);
  result.append(el('p', cats.join(', ')));
  if (report.findings.length === 0) result.append(el('p', 'No findings.'));
  const fixes = report.fixes || [];
  if (fixes.length > 0) {
    const all = 'Make ' + report.url + ' more ready for AI agents. Apply these fixes in order, then re-run the check.\n\n'
      + fixes.slice(0, 10).map((f, i) => (i + 1) + '. ' + f.prompt).join('\n\n');
    result.append(copyButton('Copy a prompt for all ' + Math.min(fixes.length, 10) + ' fixes', all));
  }
  for (const f of report.findings) {
    const d = document.createElement('details');
    const gain = f.gain > 0 ? ' (+' + f.gain.toFixed(1) + ' points)' : '';
    d.append(el('summary', f.id + ' ' + f.severity + ': ' + f.target + ' ' + f.message + gain, f.severity));
    d.append(el('p', f.fix.text));
    if (f.fix.snippet) d.append(el('pre', f.fix.snippet));
    if (f.fix.dotnet) d.append(el('pre', f.fix.dotnet));
    if (f.fix.prompt) d.append(copyButton('Copy prompt for your coding agent', f.fix.prompt));
    const a = el('a', 'spec');
    a.href = f.spec;
    d.append(a);
    result.append(d);
  }
  const link = el('a', 'Download JSON');
  link.href = URL.createObjectURL(new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' }));
  link.download = 'levelset.json';
  result.append(link);
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  statusEl.textContent = 'Checking...';
  result.replaceChildren();
  try {
    render(await run(document.getElementById('url').value));
    statusEl.textContent = '';
  } catch (err) {
    statusEl.textContent = err.message;
  }
});

if (document.modelContext && document.modelContext.registerTool) {
  document.modelContext.registerTool({
    name: 'check_site',
    description: 'Scan a public website and return its agent-readiness score out of 100 with findings and a fix for each. Use it before shipping a site that agents will read or call.',
    inputSchema: {
      type: 'object',
      properties: { url: { type: 'string', description: 'The http or https address of the site to check' } },
      required: ['url'],
    },
    outputSchema: {
      type: 'object',
      properties: {
        score: { type: 'integer', description: 'Score from 0 to 100' },
        grade: { type: 'string', description: 'Letter grade A to F' },
        fixes: { type: 'array', description: 'Failing checks ranked by points gained, each with a prompt to apply' },
        findings: { type: 'array', description: 'What to fix, each with an ID, severity and fix hint' },
      },
    },
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true },
    execute: async ({ url }) => ({ content: [{ type: 'text', text: JSON.stringify(await run(url)) }] }),
  });
}

const preset = new URLSearchParams(location.search).get('url');
if (preset) {
  document.getElementById('url').value = preset;
  form.requestSubmit();
}
