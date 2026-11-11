() => {
  const rec = window.__levelset || { mode: 'none', tools: [] };
  const tools = rec.tools.slice();
  if (rec.mode === 'native') {
    try {
      const mc = Object.getOwnPropertyDescriptor(document, 'modelContext');
      const ctx = mc && mc.get ? mc.get.call(document) : document.modelContext;
      const live = ctx && typeof ctx.getTools === 'function' ? ctx.getTools() : [];
      for (const t of live || []) if (!tools.some((x) => x.name === t.name))
        tools.push({ name: t.name, description: t.description || null, inputSchema: t.inputSchema || null, outputSchema: null, annotations: t.annotations || null, hasExecute: true, at: 0 });
    } catch { /* getTools is optional */ }
  }
  const forms = [...document.querySelectorAll('form[toolname]')].map((f) => ({
    name: f.getAttribute('toolname'),
    description: f.getAttribute('tooldescription'),
    autoSubmit: f.hasAttribute('toolautosubmit'),
    method: f.getAttribute('method') || 'get',
    fields: [...f.elements].filter((e) => e.name).map((e) => ({
      name: e.name, type: e.type, required: !!e.required,
      description: e.getAttribute('toolparamdescription') || e.getAttribute('aria-description') || (e.labels && e.labels[0] ? e.labels[0].textContent.trim() : ''),
    })),
  }));
  return JSON.stringify({
    mode: rec.mode, tools, forms,
    navigatorOnly: !!rec.navigatorAlias && !rec.documentAccess,
    textLength: (document.body ? document.body.innerText : '').replace(/\s+/g, ' ').trim().length,
  });
}
