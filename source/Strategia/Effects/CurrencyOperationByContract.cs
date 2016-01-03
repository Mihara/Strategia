﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using KSP;
using Contracts;
using Strategies;
using Strategies.Effects;

namespace Strategia
{
    /// <summary>
    /// Special CurrencyOperation that gives a modifier for specific contracts.
    /// </summary>
    public class CurrencyOperationByContract : StrategyEffect
    {
        static MethodInfo ccContractTypeMethod = null;
        static Dictionary<string, string> displayNameCache = new Dictionary<string, string>();
        static LRUCache<string, KeyValuePair<bool, Contract>> contractCache = new LRUCache<string, KeyValuePair<bool, Contract>>(100);

        List<Currency> currencies;
        string effectDescription;
        List<TransactionReasons> affectReasons;
        List<float> multipliers;
        List<string> contractTypes;

        private Vessel cachedVessel;
        private float cacheTime;
        string trait;

        static CurrencyOperationByContract()
        {
            displayNameCache["ARMContract"] = "Asteroid Resource Mining";
            displayNameCache["CollectScience"] = "Science Collection";
            displayNameCache["ExploreBody"] = "Exploration";
            displayNameCache["GrandTour"] = "Grand Tour";
            displayNameCache["PartTest"] = "Part Testing";
            displayNameCache["PlantFlag"] = "Flag Planting";
            displayNameCache["RecoverAsset"] = "Asset Recovery";
            displayNameCache["WorldFirstContract"] = "World First";
        }

        public CurrencyOperationByContract(Strategy parent)
            : base(parent)
        {
        }

        protected override string GetDescription()
        {
            float multiplier = Parent.GetLeveledListItem(multipliers);
            string multiplierStr = ToPercentage(multiplier);

            string currencyStr = currencies.Count() > 1 ? "" : (currencies.First() + " ");

            // Build the contract type list
            string first = contractTypes.First();
            string last = contractTypes.Last();
            string contractStr = ContractTypeDisplay(first);
            foreach (string contractType in contractTypes.Where(ct => ct != first && ct != last))
            {
                contractStr += ", " + ContractTypeDisplay(contractType);
            }
            if (last != first)
            {
                contractStr += " and " + ContractTypeDisplay(last);
            }

            return multiplierStr + " " + currencyStr + effectDescription +
                " for " + contractStr + " contracts when " + StringUtil.ATrait(trait) + " is present.";
        }

        protected override void OnLoadFromConfig(ConfigNode node)
        {
            base.OnLoadFromConfig(node);

            currencies = ConfigNodeUtil.ParseValue<List<Currency>>(node, "currency");
            contractTypes = ConfigNodeUtil.ParseValue<List<string>>(node, "contractType");
            effectDescription = ConfigNodeUtil.ParseValue<string>(node, "effectDescription");
            affectReasons = ConfigNodeUtil.ParseValue<List<TransactionReasons>>(node, "AffectReason");
            multipliers = ConfigNodeUtil.ParseValue<List<float>>(node, "multiplier");
            trait = ConfigNodeUtil.ParseValue<string>(node, "trait");
        }

        protected override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }

        protected override void OnRegister()
        {
            GameEvents.Modifiers.OnCurrencyModifierQuery.Add(new EventData<CurrencyModifierQuery>.OnEvent(OnEffectQuery));
            GameEvents.onVesselRecovered.Add(new EventData<ProtoVessel>.OnEvent(OnVesselRecovered));
            GameEvents.onGameSceneLoadRequested.Add(new EventData<GameScenes>.OnEvent(OnGameSceneLoadRequested));
        }

        protected override void OnUnregister()
        {
            GameEvents.Modifiers.OnCurrencyModifierQuery.Remove(new EventData<CurrencyModifierQuery>.OnEvent(OnEffectQuery));
            GameEvents.onVesselRecovered.Add(new EventData<ProtoVessel>.OnEvent(OnVesselRecovered));
            GameEvents.onGameSceneLoadRequested.Add(new EventData<GameScenes>.OnEvent(OnGameSceneLoadRequested));
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            contractCache.Clear();
            cachedVessel = null;
        }

        private void OnVesselRecovered(ProtoVessel vessel)
        {
            cachedVessel = vessel.vesselRef;
            cacheTime = Time.fixedTime;
        }

        private void OnEffectQuery(CurrencyModifierQuery qry)
        {
            // Check the reason is a match
            if (!affectReasons.Contains(qry.reason))
            {
                return;
            }

            // Check if it's non-zero
            float total = 0.0f;
            foreach (Currency currency in currencies)
            {
                total += Math.Abs(qry.GetInput(currency));
            }
            if (total < 0.01f)
            {
                return;
            }

            float funds = qry.GetInput(Currency.Funds);
            float science = qry.GetInput(Currency.Science);
            float rep = qry.GetInput(Currency.Reputation);

            string hash = string.Join("|", new string[]{
                funds.ToString(),
                science.ToString(),
                rep.ToString(),
                qry.reason.ToString()
            });

            // Check whether the contract matches the multiplier
            if (!contractCache.ContainsKey(hash))
            {
                bool foundMatch = false;
                Contract match = null;
                foreach (Contract contract in ContractSystem.Instance.Contracts.
                    Where(c => c.ContractState != Contract.State.Completed || c.DateFinished == Planetarium.fetch.time))
                {
                    // If the contract type doesn't match, don't bother 
                    if (!ContractTypeMatches(contract))
                    {
                        continue;
                    }

                    // Check contract values
                    if (qry.reason == TransactionReasons.ContractAdvance &&
                            contract.FundsAdvance == funds && science == 0.0 && rep == 0.0 ||
                        qry.reason == TransactionReasons.ContractPenalty &&
                            contract.FundsFailure == funds && science == 0.0 && contract.ReputationFailure == rep ||
                        qry.reason == TransactionReasons.ContractReward &&
                            contract.FundsCompletion == funds && contract.ScienceCompletion == science && contract.ReputationCompletion == rep)
                    {
                        foundMatch = true;
                        match = contract;
                        break;
                    }

                    // Check parameter values
                    foreach (ContractParameter parameter in contract.AllParameters)
                    {
                        if (qry.reason == TransactionReasons.ContractPenalty &&
                                parameter.FundsFailure == funds && science == 0.0 && parameter.ReputationFailure == rep ||
                            qry.reason == TransactionReasons.ContractReward &&
                                parameter.FundsCompletion == funds && parameter.ScienceCompletion == science && parameter.ReputationCompletion == rep)
                        {
                            foundMatch = true;
                            match = contract;
                            break;
                        }
                    }
                }

                contractCache[hash] = new KeyValuePair<bool, Contract>(foundMatch, match);
            }
            if (!contractCache[hash].Key)
            {
                return;
            }

            // Figure out the vessel to look at
            Vessel vessel = null;
            if (FlightGlobals.ActiveVessel != null)
            {
                vessel = FlightGlobals.ActiveVessel;
            }
            else if (cachedVessel != null && cacheTime < Time.fixedTime + 5.0f)
            {
                vessel = cachedVessel;
            }

            // Check for matching crew
            if (vessel != null)
            {
                bool crewFound = false;
                foreach (ProtoCrewMember pcm in VesselUtil.GetVesselCrew(vessel))
                {
                    if (pcm.experienceTrait.Config.Name == trait)
                    {
                        crewFound = true;
                        break;
                    }
                }
                if (!crewFound)
                {
                    return;
                }
            }

            float multiplier = Parent.GetLeveledListItem(multipliers);
            foreach (Currency currency in currencies)
            {
                qry.AddDelta(currency, multiplier * qry.GetInput(currency) - qry.GetInput(currency));
            }
        }

        private bool ContractTypeMatches(Contract contract)
        {
            // Get the contract type
            Type contractType = contract.GetType();
            string contractTypeName = contractType.Name;
            if (contractTypeName == "ConfiguredContract")
            {
                if (ccContractTypeMethod == null)
                {
                    ccContractTypeMethod = contractType.GetMethod("contractGroupName", new Type[] { typeof(Contract) });
                }
                contractTypeName = (string)ccContractTypeMethod.Invoke(null, new object[] { contract });
            }

            // Check if contract type matches
            return contractTypes.Contains(contractTypeName);
        }

        private string ContractTypeDisplay(string contractTypeName)
        {
            if (!displayNameCache.ContainsKey(contractTypeName))
            {
                string displayName = null;

                // Contract Configurator integration
                Type contractGroup = TypeUtil.FindType("ContractGroup");
                Debug.Log("XXX contractGroup = " + contractGroup);
                if (contractGroup != null)
                {
                    MethodInfo displayNameMethod = contractGroup.GetMethod("GroupDisplayName");
                    displayName = (string)displayNameMethod.Invoke(null, new object[] { contractTypeName });
                }

                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = contractTypeName.Replace("Contract", "");
                }

                displayNameCache[contractTypeName] = displayName;
            }

            return displayNameCache[contractTypeName];
        }
    }
}