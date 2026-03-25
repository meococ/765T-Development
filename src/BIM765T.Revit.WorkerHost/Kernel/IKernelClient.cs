using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.WorkerHost.Kernel;

internal interface IKernelClient
{
    Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken);
}
