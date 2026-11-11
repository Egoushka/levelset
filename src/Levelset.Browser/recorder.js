// Levelset recorder shim. Runs before any page script. It records tool descriptors and never calls a tool.
(() => {
  if (window.__levelset) return;
  const t0 = performance.now();
  const rec = { mode: 'shim', tools: [], navigatorAlias: false, documentAccess: false };
  window.__levelset = rec;
  const clone = (v) => { try { return v === undefined ? null : JSON.parse(JSON.stringify(v)); } catch { return null; } };
  const record = (tool) => {
    if (!tool || typeof tool !== 'object') return;
    const d = {
      name: typeof tool.name === 'string' ? tool.name : null,
      description: typeof tool.description === 'string' ? tool.description : null,
      inputSchema: clone(tool.inputSchema),
      outputSchema: clone(tool.outputSchema),
      annotations: clone(tool.annotations),
      hasExecute: typeof tool.execute === 'function',
      at: Math.round(performance.now() - t0),
    };
    const i = rec.tools.findIndex((x) => x.name === d.name);
    if (i >= 0) rec.tools[i] = d; else rec.tools.push(d);
  };

  const native = document.modelContext;
  let context;
  if (native && typeof native.registerTool === 'function') {
    rec.mode = 'native';
    const orig = native.registerTool.bind(native);
    native.registerTool = (tool, ...rest) => { record(tool); return orig(tool, ...rest); };
    if (typeof native.provideContext === 'function') {
      const origProvide = native.provideContext.bind(native);
      native.provideContext = (ctx, ...rest) => { (ctx && ctx.tools || []).forEach(record); return origProvide(ctx, ...rest); };
    }
    context = native;
  } else {
    context = Object.assign(new EventTarget(), {
      registerTool(tool) { record(tool); return { unregister() { rec.tools = rec.tools.filter((t) => t.name !== (tool && tool.name)); } }; },
      unregisterTool(name) { rec.tools = rec.tools.filter((t) => t.name !== name); },
      provideContext(ctx) { rec.tools = []; ((ctx && ctx.tools) || []).forEach(record); },
      getTools() { return rec.tools.map((t) => ({ name: t.name, description: t.description, inputSchema: t.inputSchema })); },
      executeTool() { throw new Error('Levelset records tool descriptors and never executes a tool'); },
    });
  }
  Object.defineProperty(document, 'modelContext', { configurable: true, get() { rec.documentAccess = true; return context; } });
  if (!('modelContext' in navigator)) {
    Object.defineProperty(navigator, 'modelContext', { configurable: true, get() { rec.navigatorAlias = true; return context; } });
  }
})();
