using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Path
{
    public class ClientPathExplorerProgressReportHandler : IMessageHandler<IWorldSession, ClientPathExplorerProgressReport>
    {
        public void HandleMessage(IWorldSession session, ClientPathExplorerProgressReport progressReport)
        {
            session.Player?.PathManager.ReportExplorerProgress(progressReport.PathMissionId, progressReport.ExplorerNodeIndex);
        }
    }
}
