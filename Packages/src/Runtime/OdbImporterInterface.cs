using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace oojjrs.odb
{
    public interface OdbImporterInterface
    {
        Task ImportAsync(OdbImportSession session, Stream source, CancellationToken cancellationToken);
    }
}
