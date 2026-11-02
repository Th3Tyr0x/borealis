using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Borealis.Core.Diagnostics;
using Borealis.Core.Ipc;

namespace Borealis.Service;

/// <summary>
/// Named-Pipe-Server für die Tray-App. Angemeldete Benutzer dürfen lesen und schreiben, damit die App ohne
/// Administratorrechte läuft; Verbindungen werden nacheinander bedient.
/// </summary>
sealed class PipeServer(BorealisRuntime runtime)
{
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var pipe = Create();
            try
            {
                await pipe.WaitForConnectionAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var request = await PipeProtocol.ReadAsync<PipeRequest>(pipe, timeout.Token);
                if (request == null) continue;
                var response = await runtime.HandleAsync(request, timeout.Token);
                await PipeProtocol.WriteAsync(pipe, response, timeout.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Text.Json.JsonException)
            {
                Log.Warn($"Pipe-Verbindung abgebrochen: {ex.Message}");
            }
        }
    }

    static NamedPipeServerStream Create()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeProtocol.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }
}
