﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;
using KSPAchievements;
using Strategies;
using Strategies.Effects;

namespace Strategia
{
    public class VesselEnrouteRequirement : StrategyEffect, IRequirementEffect
    {
        // Use 2.5 billion meters as the distance threshold (about 50 Duna SOIs)
        const double distanceLimit = 2500000000;

        CelestialBody body;
        public bool invert;

        public VesselEnrouteRequirement(Strategy parent)
            : base(parent)
        {
        }

        protected override void OnLoadFromConfig(ConfigNode node)
        {
            body = ConfigNodeUtil.ParseValue<CelestialBody>(node, "body");
            invert = ConfigNodeUtil.ParseValue<bool>(node, "invert", false);
        }

        public string RequirementText()
        {
            return "Must " + (invert ? "not have any vessels" : "have a vessel") + " en route to " + body.theName;
        }

        public bool RequirementMet(out string unmetReason)
        {
            unmetReason = null;

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                bool enRoute = VesselIsEnroute(vessel);
                if (enRoute && invert)
                {
                    unmetReason = vessel.vesselName + " is en route to " + body.theName;
                    return false;
                }
                else if (enRoute && !invert)
                {
                    return true;
                }
            }

            if (invert)
            {
                return true;
            }
            else
            {
                unmetReason = "No vessels are en route to " + body.theName;
                return false;
            }
        }

        protected bool VesselIsEnroute(Vessel vessel)
        {
            // Only check when in orbit of the sun
            if (vessel.mainBody != FlightGlobals.Bodies[0])
            {
                return false;
            }

            // Ignore escaping or other silly things
            if (vessel.situation != Vessel.Situations.ORBITING)
            {
                return false;
            }

            // Asteroids?  No...
            if (vessel.vesselType == VesselType.SpaceObject)
            {
                return false;
            }

            // Check the orbit
            Orbit vesselOrbit = vessel.loaded ? vessel.orbit : vessel.protoVessel.orbitSnapShot.Load();
            Orbit bodyOrbit = body.orbit;
            double minUT = Planetarium.GetUniversalTime();
            double maxUT = minUT + vesselOrbit.period;
            double UT = (maxUT - minUT) / 2.0;
            int iterations = 0;
            double distance = Orbit.SolveClosestApproach(vesselOrbit, bodyOrbit, ref UT, (maxUT - minUT) * 0.3, 0.0, minUT, maxUT, 0.1, 50, ref iterations);

            return distance < distanceLimit;
        }
    }
}