﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;
using Strategies;

namespace Strategia
{
    /// <summary>
    /// Special ScenarioModule to notify on strategy availability changes.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToExistingCareerGames | ScenarioCreationOptions.AddToNewCareerGames,
        GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    public class StrategyNotifier : ScenarioModule
    {
        Dictionary<string, bool> strategyActive = new Dictionary<string, bool>();
        Dictionary<string, bool> messageStrategies = new Dictionary<string, bool>();

        void Start()
        {
            DontDestroyOnLoad(this);
            StartCoroutine(CheckStrategyState());
        }

        IEnumerator<YieldInstruction> CheckStrategyState()
        {
            float timeStep = 0.01f;
            float pauseTime = 0.05f;

            while (true)
            {
                // Pause until the strategy system is ready
                while (StrategySystem.Instance == null)
                {
                    yield return new WaitForSeconds(5);
                }

                float startTime = Time.realtimeSinceStartup;

                foreach (StrategiaStrategy strategy in StrategySystem.Instance.Strategies.OfType<StrategiaStrategy>())
                {
                    bool met = true;

                    // Check admin building level
                    int minFacilityLevel = strategy.MinimumFacilityLevel();
                    if (minFacilityLevel > 1)
                    {
                        int currentLevel = (int)ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.Administration) *
                            ScenarioUpgradeableFacilities.GetFacilityLevelCount(SpaceCenterFacility.Administration) + 1;
                        met &= currentLevel >= minFacilityLevel;
                    }

                    // Check Reputation
                    if (strategy.RequiredReputation > -1000)
                    {
                        float currentReputation = Reputation.Instance.reputation;
                        met &= Math.Round(currentReputation) >= Math.Round(strategy.RequiredReputation);
                    }

                    // Check effects
                    foreach (StrategyEffect effect in strategy.Effects)
                    {
                        if (!met)
                        {
                            break;
                        }

                        // Check if a requirement is met
                        IRequirementEffect requirement = effect as IRequirementEffect;
                        if (requirement != null)
                        {
                            string unmetReason;
                            met = requirement.RequirementMet(out unmetReason);
                        }

                        // Check if we need to take a break
                        if (Time.realtimeSinceStartup >= startTime + timeStep)
                        {
                            yield return null;
                            startTime = Time.realtimeSinceStartup;
                        }
                    }

                    if (!strategyActive.ContainsKey(strategy.Config.Name))
                    {
                        strategyActive[strategy.Config.Name] = met;
                    }
                    else if (strategyActive[strategy.Config.Name] != met)
                    {
                        Notify(strategy, met);
                        strategyActive[strategy.Config.Name] = met;
                    }

                    yield return new WaitForSeconds(pauseTime);
                }

                // Pause at least 5 seconds between full checks
                yield return new WaitForSeconds(5);
            }
        }

        protected void Notify(StrategiaStrategy strategy, bool requirementsMet)
        {
            const string title = "Strategy Availability Changed";

            // Check for an existing message
            MessageSystem.Message message = MessageSystem.Instance.FindMessages(m => m.messageTitle == title).FirstOrDefault();
            if (message != null)
            {
                message.IsRead = false;
            }
            else
            {
                messageStrategies = new Dictionary<string, bool>();
            }

            // Add our strategy to the list
            messageStrategies[strategy.Title] = requirementsMet;

            // Build the message
            string msg = "";
            if (messageStrategies.Any(p => p.Value))
            {
                msg += "<b>New strategies available:</>\n";
            }
            foreach (string strategyTitle in messageStrategies.Where(p => p.Value).Select(p => p.Key))
            {
                msg += "    " + strategyTitle + "\n";
            }
            if (messageStrategies.Any(p => !p.Value))
            {
                if (!string.IsNullOrEmpty(msg))
                {
                    msg += "\n";
                }
                msg += "<b>Strategies no longer available:</>\n";
            }
            foreach (string strategyTitle in messageStrategies.Where(p => !p.Value).Select(p => p.Key))
            {
                msg += "    " + strategyTitle + "\n";
            }

            if (message == null)
            {
                MessageSystem.Instance.AddMessage(new MessageSystem.Message(title, msg,
                    MessageSystemButton.MessageButtonColor.BLUE, MessageSystemButton.ButtonIcons.ALERT));
            }
            else
            {
                message.message = msg;
            }
        }

        public override void OnSave(ConfigNode node)
        {
            // Save current strategy state so we can detect a change
            ConfigNode strategyNode = new ConfigNode("STRATEGIES");
            node.AddNode(strategyNode);
            foreach (KeyValuePair<string, bool> pair in strategyActive)
            {
                string strategy = pair.Key;
                bool active = pair.Value;
                strategyNode.AddValue(strategy, active);
            }

            // Save strategies in the message so that we can more easily change it
            ConfigNode msgNode = new ConfigNode("MSG_STRATEGIES");
            node.AddNode(msgNode);
            foreach (KeyValuePair<string, bool> pair in messageStrategies)
            {
                string strategy = pair.Key;
                bool active = pair.Value;
                strategyNode.AddValue(strategy, active);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            // Load the current strategy states
            ConfigNode strategyNode = node.GetNode("STRATEGIES");
            if (strategyNode != null)
            {
                foreach (ConfigNode.Value pair in strategyNode.values)
                {
                    strategyActive[pair.name] = bool.Parse(pair.value);
                }
            }

            // Load the current strategies in the message
            ConfigNode msgNode = node.GetNode("MSG_STRATEGIES");
            if (msgNode != null)
            {
                foreach (ConfigNode.Value pair in msgNode.values)
                {
                    messageStrategies[pair.name] = bool.Parse(pair.value);
                }
            }
        }
    }
}
