using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace oojjrs.odb
{
    public sealed class OdbXmlSnapshotImporter : OdbImporterInterface
    {
        Task OdbImporterInterface.ImportAsync(OdbImportSession session, Stream source, CancellationToken cancellationToken)
        {
            return OdbXmlSnapshot.ImportAsync(session, source, cancellationToken);
        }
    }
}
