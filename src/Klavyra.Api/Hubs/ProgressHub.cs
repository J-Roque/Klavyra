using Microsoft.AspNetCore.SignalR;

namespace Klavyra.Api.Hubs;

/// <summary>Server → browser only: broadcasts a ProjectDto every time a transcription advances.</summary>
public sealed class ProgressHub : Hub
{
    public const string ProgressEvent = "projectUpdated";
}
