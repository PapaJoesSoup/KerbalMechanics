﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KerbalMechanics
{
    class ModuleReliabilityTank : ModuleReliabilityBase
    {
        //KSP FIELDS
        #region KSP FIELDS
        /// <summary>
        /// Chance to fail while running under perfect conditions
        /// </summary>
        [KSPField]
        public double chanceToFailPerfect = 0.00001f;
        /// <summary>
        /// Chance to fail while running under the worst of conditions
        /// </summary>
        [KSPField]
        public double chanceToFailTerrible = 0.001f;

        /// <summary>
        /// The amount the leaking resource is exponentially reduced by.
        /// </summary>
        [KSPField(isPersistant = true, guiActive = false)]
        public float pole = 0.1f;

        /// <summary>
        /// The maximum amount pole can be set to.
        /// </summary>
        [KSPField]
        public float maxTC = 60f;
        /// <summary>
        /// The minimum amount pole can be set to.
        /// </summary>
        [KSPField]
        public float minTC = 10f;
        #endregion

        //PROPERTIES
        #region PROPERTIES
        /// <summary>
        /// Gets the current running chance to fail, based on the current reliability.
        /// </summary>
        public double CurrentChanceToFail
        {
            get { return chanceToFailPerfect + ((chanceToFailTerrible - chanceToFailPerfect) * (1f - reliability)); }
        }
        /// <summary>
        /// The name of the module. Held for use by the module injecter.
        /// </summary>
        public override string ModuleName
        {
            get { return "Tank"; }
        }
        #endregion

        //OTHER VARS
        #region OTHER VARS
        /// <summary>
        /// The list of leakable resources on this part.
        /// </summary>
        List<PartResource> leakables;
        /// <summary>
        /// Can this part leak?
        /// </summary>
        bool canLeak = true;
        /// <summary>
        /// The resource currently leaking.
        /// </summary>
        PartResource leak;

        /// <summary>
        /// The duct tape sound.
        /// </summary>
        FXGroup ductTapeSound;
        #endregion

        //KSP METHODS
        #region KSP METHODS
        /// <summary>
        /// Called when the part is started.
        /// </summary>
        /// <param name="state">The start state.</param>
        public override void OnStart(PartModule.StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsFlight)
            {
                leakables = GetLeakables();

                SoundManager.LoadSound(KMUtil.soundSource + "Tape", "Tape");
                ductTapeSound = new FXGroup("ductTapeSound");
                SoundManager.CreateFXSound(part, ductTapeSound, "Tape", false, 50f);

                if (failure != "" && leakables != null)
                {
                    string leakName = failure.Substring(0, failure.Length - " leaking!".Length);

                    foreach (PartResource r in leakables)
                    {
                        if (r.resourceName == leakName)
                        {
                            leak = r;
                            Events["FixLeak"].active = true;
                            Events["ApplyDuctTape"].active = true;
                            break;
                        }
                    }

                    if (!leak)
                    {
                        Logger.DebugError("Leak \"" + leakName + "\" not found!");
                        canLeak = false;
                        failure = "";
                    }
                }
            }
        }

        ///// <summary>
        ///// Called when the part is updated.
        ///// </summary>
        public override void OnUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (failure == "")
                {
                    if (timeSinceFailCheck < timeTillFailCheck)
                    {
                        timeSinceFailCheck += TimeWarp.deltaTime;
                    }
                    else
                    {
                        timeSinceFailCheck = 0f;
                        reliability -= CurrentReliabilityDrain;

                        if (Random.Range(0f, 1f) < CurrentChanceToFail && canLeak)
                        {
                            Leak();
                        }
                    }
                }
                else
                {
                    double amount = pole * leak.amount * (rocketPartsLeftToFix / rocketPartsNeededToFix) * TimeWarp.deltaTime;
                    part.RequestResource(leak.resourceName, amount);
                }
            }

            base.OnUpdate();
        }
        #endregion

        //KSP EVENTS
        #region KSP EVENTS
        /// <summary>
        /// Fixes the leak, using the needed amount of rocket parts to fix.
        /// </summary>
        [KSPEvent(active = false, guiName = "Patch Leak", guiActive = false, guiActiveUnfocused = true, unfocusedRange = 3f, externalToEVAOnly = true)]
        public void FixLeak()
        {
            Part kerbal = FlightGlobals.ActiveVessel.parts[0];

            rocketPartsLeftToFix -= (int)kerbal.RequestResource("RocketParts", (double)System.Math.Min(rocketPartsLeftToFix, 10));

            fixSound.audio.Play();

            if (rocketPartsLeftToFix <= 0)
            {
                Events["FixLeak"].active = false;
                Events["ApplyDuctTape"].active = false;

                leak = null;
                failure = "";
                reliability = 1f;

                broken = false;
            }
        }
        /// <summary>
        /// Applies duct tape, stopping the leak, but not completely fixing it.
        /// </summary>
        [KSPEvent(active = false, guiName = "Apply Duct Tape", guiActive = false, guiActiveUnfocused = true, unfocusedRange = 3f, externalToEVAOnly = true)]
        public void ApplyDuctTape()
        {
            Events["FixLeak"].active = false;
            Events["ApplyDuctTape"].active = false;

            leak = null;
            failure = "";
            reliability += 0.1f;

            reliability = reliability.Clamp(0, 1);

            ductTapeSound.audio.Play();

            broken = false;
        }

        [KSPEvent(active = false, guiActive = false, guiActiveEditor = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 3f, guiName = "Perform Maintenance")]
        public override void PerformMaintenance()
        {
            if (FlightGlobals.ActiveVessel.isEVA)
            {
                Part kerbal = FlightGlobals.ActiveVessel.parts[0];

                rocketPartsLeftToFix -= (int)kerbal.RequestResource("RocketParts", (double)System.Math.Min(rocketPartsLeftToFix, 2));

                fixSound.audio.Play();

                reliability += 0.1;
                reliability = reliability.Clamp(0, 1);
            }
        }
        #endregion

        //OTHER METHODS
        #region OTHER METHODS
        /// <summary>
        /// Gets the resources attached to this part that can leak.
        /// </summary>
        /// <returns>The list of resources attached to this part that can leak.</returns>
        List<PartResource> GetLeakables()
        {
            List<string> blackList = null;
            if (HighLogic.LoadedSceneIsFlight)
            {
                blackList = FlightLoader.Instance.LeakBlackList;
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                blackList = EditorLoader.Instance.LeakBlackList;
            }
            else
            {
                Logger.DebugError("Can't get leakables! Scene not recongnized!");
                return null;
            }

            List<PartResource> result = part.Resources.list.FindAll(r => !blackList.Contains(r.resourceName));

            return result;
        }

        void Leak()
        {
            if (!broken && leakables != null && leakables.Count > 0)
            {
                // Choose a random severity of the leak
                float TC = UnityEngine.Random.Range(minTC, maxTC);
                this.pole = 1 / TC;

                // Pick a random index to leak.
                int idx = (leakables.Count == 1) ? 0 : UnityEngine.Random.Range(0, leakables.Count);

                leak = leakables[idx];

                this.failure = leak.resourceName + " leaking!";

                rocketPartsLeftToFix = rocketPartsNeededToFix;

                Events["FixLeak"].active = true;
                Events["ApplyDuctTape"].active = true;

                KMUtil.PostFailure(part, " has begun leaking " + leak.resourceName + "!");

                broken = true;
            }
        }

        /// <summary>
        /// Gets the reliability information on this module.
        /// </summary>
        public override void DisplayDesc()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            GUILayout.Label("Reliability:", HighLogic.Skin.label);
            GUILayout.Label(" ", HighLogic.Skin.label);
            GUILayout.Label("Chances of failure:", HighLogic.Skin.label);
            GUILayout.Label("@100%:", HighLogic.Skin.label);
            GUILayout.Label("@0%:", HighLogic.Skin.label);
            GUILayout.Label(" ", HighLogic.Skin.label);
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label(reliability.ToString("##0.#%"), HighLogic.Skin.label);
            GUILayout.Label(" ", HighLogic.Skin.label);
            GUILayout.Label(" ", HighLogic.Skin.label);
            GUILayout.Label(chanceToFailPerfect.ToString("##0.#####%"), HighLogic.Skin.label);
            GUILayout.Label(chanceToFailTerrible.ToString("##0.#####%"), HighLogic.Skin.label);
            GUILayout.Label(" ", HighLogic.Skin.label);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }
        #endregion
    }
}