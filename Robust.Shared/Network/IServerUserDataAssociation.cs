using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Robust.Shared.Network;

/// <summary>
/// Provides a mechanism for Content to determine which user a public key should map to.
/// This allows accessing the database, as well as letting servers specify their own
/// mapping logic should they wish to.
///
/// (This is server-only, but the interface is in shared since NetManager.ServerAuth is in shared.)
/// </summary>
public interface IServerUserDataAssociation
{
    public Task<AssociationResult> AttemptUserDataFromPublicKey(ImmutableArray<byte> publicKey, ImmutableArray<byte> hWId, string requestedUserName);

    public struct AssociationResult
    {
        public AssociationResult(bool success, NetUserData? userData, string errorMessage = "")
        {
            this.success = success;
            this.userData = userData;
            this.errorMessage = errorMessage;
        }

        public bool success;
        public NetUserData? userData;
        public string errorMessage;
    }
}
