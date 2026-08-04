using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace oojjrs.odb
{
    public interface OdbExporterInterface
    {
        Task ExportAsync(OdbExportSession session, Stream destination, CancellationToken cancellationToken);
    }
}
