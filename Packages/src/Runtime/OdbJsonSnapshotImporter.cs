using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace oojjrs.odb
{
    public sealed class OdbJsonSnapshotImporter : OdbImporterInterface
    {
        Task OdbImporterInterface.ImportAsync(OdbImportSession session, Stream source, CancellationToken cancellationToken)
        {
            return OdbJsonSnapshot.ImportAsync(session, source, cancellationToken);
        }
    }
}
