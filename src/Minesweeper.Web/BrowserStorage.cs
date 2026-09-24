using Microsoft.JSInterop;
using Minesweeper.Core;

namespace Minesweeper.Web;

/// <summary>localStorage through ms2.js. WebAssembly runs in the page, so the calls can be synchronous.</summary>
public sealed class BrowserStorage : IKeyValueStore
{
    private readonly IJSInProcessRuntime _js;

    public BrowserStorage(IJSInProcessRuntime js) => _js = js;

    /// <summary>False when the browser blocks storage (some private modes), so progress cannot be kept.</summary>
    public static bool IsAvailable(IJSInProcessRuntime js) => js.Invoke<bool>("ms2.storage.available");

    public IEnumerable<string> Keys => _js.Invoke<string[]>("ms2.storage.keys");

    public string? Get(string key) => _js.Invoke<string?>("ms2.storage.get", key);

    public bool TrySet(string key, string value, out string error)
    {
        error = _js.Invoke<string>("ms2.storage.set", key, value);
        return error.Length == 0;
    }

    public void Remove(string key) => _js.InvokeVoid("ms2.storage.remove", key);
}
