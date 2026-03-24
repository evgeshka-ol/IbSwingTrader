using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class WishListEvaluationSettingsProvider : IWishListEvaluationSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider;

        public WishListEvaluationSettingsProvider(IAgentSettingsProvider agentSettingsProvider)
        {
            _agentSettingsProvider = agentSettingsProvider;
        }

        public WishListEvaluationSettings Get()
        {
            return _agentSettingsProvider.Get().WishListEvaluation;
        }
    }
}