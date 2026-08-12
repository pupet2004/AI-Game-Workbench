using System.Text.Json;

namespace Workbench.Runtime.Providers.Codex;

internal sealed record CodexProtocolMessage(string Method, JsonElement Params);

internal sealed record CodexServerRequest(JsonElement Id, string Method, JsonElement Params);
