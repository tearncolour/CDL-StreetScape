using System;
using System.Linq;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Offline checks of the snapping kernel. Prints RESULT=PASS or RESULT=FAIL.</summary>
    internal static class Program
    {
        private static readonly List<string> s_Failures = new List<string>();
        private static int s_Checks;

        private static int Main(string[] args)
        {
            // As with the Realistic Information Extension (0.12.29): the live screens are tested as they work with it; LiveContentTests tests them without.
            CDLC.TS.Geo.LiveContent.On = true;
            if (args.Length == 1 && args[0] == "overroad")
            {
                OverRoadTests.RunAll();
                OverHungTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "supportshift")
            {
                SupportShiftTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "midroad")
            {
                MidRoadTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "stylepick")
            {
                StylePickTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "takeover")
            {
                TakeOverTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "saverecords")
            {
                SaveRecordTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "footprint")
            {
                FootprintTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "viewdistance")
            {
                TileStreamerTests.RunAll();
                PreferencesTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "roadside")
            {
                RoadsideSnapTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "breakbridge")
            {
                BreakBridgeTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "jogprofile")
            {
                JogProfileTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "pathbudget")
            {
                PathBudgetTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "spurs")
            {
                SpurTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "chainoverlap")
            {
                ChainOverlapTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "roadundercursor")
            {
                RoadUnderCursorTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "extensionregistry")
            {
                ExtensionRegistryTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "albedocap")
            {
                AlbedoCapTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "postground")
            {
                PostGroundTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "markingpick")
            {
                MarkingPickTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "autoowner")
            {
                AutoOwnerTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "carrypick")
            {
                CarryPickTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "noisewall")
            {
                SurfaceHeightsTests.RunAll();
                NoiseWallTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "runcut")
            {
                RunCutTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "kerbline")
            {
                KerbLineTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "kerbtab")
            {
                KerbTabTests.RunAll();
                return Summary();
            }

            if (args.Length == 2 && args[0] == "kerbline-write")
                return KerbLinePrint.Write(args[1]);

            if (args.Length == 1 && args[0] == "decal")
            {
                DecalSettingsTests.RunAll();
                GameShaderTests.RunAll();
                return Summary();
            }

            if (args.Length == 2 && args[0] == "runcut-write")
                return RunCutPrint.Write(args[1]);

            if (args.Length == 2 && args[0] == "overface-write")
                return OverRoadFacePrint.Write(args[1]);

            if (args.Length == 2 && args[0] == "printlightingfull")
            {
                System.IO.File.WriteAllLines(args[1], LightingPrint.FullLines(), new System.Text.UTF8Encoding(false));
                return 0;
            }

            if (args.Length == 1 && args[0] == "markflat")
            {
                SurfaceNormalsTests.GoldenOnly();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "marknormal")
            {
                SurfaceNormalsTests.RunAll();
            PaintExperimentTests.RunAll();
                return Summary();
            }

            if (args.Length == 2 && args[0] == "markingflat-write")
                return MarkingFlatPrint.Write(args[1]);

            if (args.Length == 1 && args[0] == "citysigns")
            {
                CitySignTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "followwall")
            {
                FollowWallSim.Print();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "followdrop")
            {
                FollowDropSim.Print();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "follow")
            {
                FollowFastTests.RunAll();
                FollowSchedulerTests.RunAll();
                RoadRelationTests.RunAll();
                FollowWatchTests.RunAll();
                FollowWriteTests.RunAll();
                FollowSpeedTests.RunAll();
                FollowPhaseTests.RunAll();
                ElevatedFollowTests.RunAll();
                PreferencesTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "elevfollow")
            {
                ElevatedFollowTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "picturecut")
            {
                PictureContourTests.RunAll();
                LoopClipTests.RunAll();
                PictureCutTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "fit")
            {
                FitWorkerTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "wallheight")
            {
                WallHeightTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "multi-budget")
                return MultiBudgetProbe.Run();

            if (args.Length > 1 && args[0] == "multi-contours")
                return MultiBudgetProbe.Contours(args.Skip(1).ToArray());

            if (args.Length == 2 && args[0] == "dump")
                return BoardDump.Write(args[1]);

            if (args.Length == 2 && args[0] == "lighting-print")
                return LightingPrint.Write(args[1]);

            if (args.Length == 1 && args[0] == "loadpace")
            {
                LoadGateTests.RunAll();
                LoadPaceTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "textbudget")
            {
                TextBudgetTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "english")
            {
                NativeEnglishTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "holdbudget")
            {
                HoldBudgetTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "ecsaudit")
            {
                EcsAuditTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "census")
            {
                ClearCensusTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "trace")
            {
                ClearTraceTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "clearlate")
            {
                ClearLateTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "bilingual")
            {
                BilingualTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "stand")
            {
                StandPlannerTests.RunAll();
                StandBoxTests.RunAll();
                StandHeavyTests.RunAll();
                TextTiltTests.RunAll();
                DepressionTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "texttilt")
            {
                StandPlannerTests.RunAll();
                StandHeavyTests.RunAll();
                TextTiltTests.RunAll();
                DepressionTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "lighting")
            {
                LightingTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "citybarrier")
            {
                CityBarrierTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "morebarrier")
            {
                MoreBarrierTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "otherhosts")
            {
                OtherHostFollowTests.RunAll();
                OtherHostTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "toll")
            {
                TollTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "vanillaclear")
            {
                VanillaClearTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "textfar")
            {
                TextFarTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "textbox")
            {
                TextBoxTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "unicode")
            {
                UnicodeTests.RunAll();
                EmojiFontTests.RunAll();
                EmojiPlanTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "textfonts")
            {
                TextFontTests.RunAll();
                TextStyleTests.RunAll();
                TextEffectTests.RunAll();
                TextArcTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "planners")
            {
                PlannerPerfTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "boundary")
            {
                // The font that the figures and the characters are made of is read by the lettering tests.
                FontLetterTests.RunAll();
                RoadPostTests.RunAll();
                BoundaryPostTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "fluorescent")
            {
                FluorescentTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "turn")
            {
                TurnAdjustTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "typespeed")
            {
                TypeSpeedTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "geomguard")
            {
                GeometryGateTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "spacegantry")
            {
                SpaceGantryTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "limitframe")
            {
                LimitFrameTests.RunAll();
                return Summary();
            }

            if (args.Length == 1 && args[0] == "gantry-golden")
            {
                SpaceGantryTests.PrintGolden();
                return 0;
            }

            Run("curve: points, tangents and closest point on a line", StraightCurve);
            Run("curve: closest point on a bend is on the bend", BentCurve);
            Run("curve: arc table agrees with the geometry", ArcLengths);
            Run("curve: a collapsed end still has a heading", CollapsedEnd);
            Run("edge snap: side, offset, facing and yaw on a straight road", EdgeStraight);
            Run("edge snap: the forced side and the margin", EdgeOptions);
            Run("edge snap: left-hand traffic reverses the facing", LeftHandTraffic);
            Run("edge snap: follows a bend", EdgeBend);
            Run("edge snap: a cursor far from the road is flagged", EdgeFar);
            Run("approach: the setback is measured from the end", ApproachSetback);
            Run("marking: dashes of a broken line", DashPattern);
            Run("marking: phase, solid lines and guards", DashEdgeCases);
            Run("marking: an offset line keeps its distance on a bend", OffsetOnBend);
            GeoTests.RunAll();
            AssemblyTests.RunAll();
            AttachTests.RunAll();
            DoubleCantileverTests.RunAll();
            LedTests.RunAll();
            TrafficMapTests.RunAll();
            TrafficWalkTests.RunAll();
            TsMarkersTests.RunAll();
            RawStatsTests.RunAll();
            JunctionShapeTests.RunAll();
            CityBandTests.RunAll();
            StagingTests.RunAll();
            AttachedTests.RunAll();
            EdgeHintTests.RunAll();
            ItemStoreTests.RunAll();
            TileStreamerTests.RunAll();
            CacheTests.RunAll();
            ReducedDetailTests.RunAll();
            FaceDetailTests.RunAll();
            TextureBudgetTests.RunAll();
            ConcurrencyTests.RunAll();
            InstanceCacheTests.RunAll();
            ExpiringCacheTests.RunAll();
            HintEqualityTests.RunAll();
            VmsAutoTests.RunAll();
            VmsAutoSpeedLaneTests.RunAll();
            TsAheadTests.RunAll();
            RoadMarkingTests.RunAll();
            PolygonArrowTests.RunAll();
            MarkingLineTests.RunAll();
            MarkingFigureTests.RunAll();
            MarkingWarningTests.RunAll();
            StandardComplianceTests.RunAll();
            RunPathTests.RunAll();
            FontLetterTests.RunAll();
            MarkingCatalogTests.RunAll();
            MarkingBuildTests.RunAll();
            MarkingPlaceTests.RunAll();
            MarkingSymbolTests.RunAll();
            MarkingExtraTests.RunAll();
            MarkingAreaTests.RunAll();
            PolygonFillTests.RunAll();
            VanillaClearTests.RunAll();
            ClearLayerTests.RunAll();
            WaitingAreaTests.RunAll();
            RoadWidthTests.RunAll();
            KerbMarginTests.RunAll();
            MidRoadTests.RunAll();
            StylePickTests.RunAll();
            CarryPickTests.RunAll();
            SupportShiftTests.RunAll();
            ShortcutPolicyTests.RunAll();
            CityBoardTests.RunAll();
            PlaceNameTests.RunAll();
            WallAndPostTests.RunAll();
            RoadChainTests.RunAll();
            AutoMoreTests.RunAll();
            AutoOwnerTests.RunAll();
            JunctionReadTests.RunAll();
            PictureContourTests.RunAll();
            LoopClipTests.RunAll();
            PictureCutTests.RunAll();
            SlowPolicyTests.RunAll();
            MaterialGateTests.RunAll();
            PerfStatsTests.RunAll();
            SignHashIndexTests.RunAll();
            RoundGateTests.RunAll();
            ArcLayoutTests.RunAll();
            TextSupportTests.RunAll();
            TextMountTests.RunAll();
            FacadeMoveTests.RunAll();
            RevisionSlotTests.RunAll();
            WarmSetTests.RunAll();
            LogRotationTests.RunAll();
            GuideTests.RunAll();
            ImageTests.RunAll();
            UiTextTests.RunAll();
            AreaFitTests.RunAll();
            FitWorkerTests.RunAll();
            PostGroundTests.RunAll();
            MarkingPickTests.RunAll();
            AlbedoCapTests.RunAll();
            BoardTurnTests.RunAll();
            HungReachTests.RunAll();
            BarrierTests.RunAll();
            BarrierAutoTests.RunAll();
            SurfaceHeightsTests.RunAll();
            NoiseWallTests.RunAll();
            KerbSnapTests.RunAll();
            FreeTextTests.RunAll();
            GantryFaceTests.RunAll();
            BrtTests.RunAll();
            CabinetTests.RunAll();
            HeightTests.RunAll();
            StripPostTests.RunAll();
            FCantileverTests.RunAll();
            TollTests.RunAll();
            BlockCompressTests.RunAll();
            NearFirstTests.RunAll();
            CustomSignTests.RunAll();
            LiveContentTests.RunAll();
            ExtensionRegistryTests.RunAll();
            RoadUnderCursorTests.RunAll();
            SpurTests.RunAll();
            PathBudgetTests.RunAll();
            JogProfileTests.RunAll();
            TakeOverTests.RunAll();
            BreakBridgeTests.RunAll();
            RoadsideSnapTests.RunAll();
            FootprintTests.RunAll();
            CutAndGroundTests.RunAll();
            MarkingGroundTests.RunAll();
            KerbLineTests.RunAll();
            KerbTabTests.RunAll();
            CityBarrierTests.RunAll();
            DetailTests.RunAll();
            KerbTests.RunAll();
            BendTests.RunAll();
            HistoryTests.RunAll();
            OtherHostTests.RunAll();
            OtherHostFollowTests.RunAll();
            TiltCrankTests.RunAll();
            FinishTests.RunAll();
            RootPaintTests.RunAll();
            OctagonTests.RunAll();
            IntegratedTests.RunAll();
            PlatePostTests.RunAll();
            FramedTests.RunAll();
            WayfindingTests.RunAll();
            ArmAdjustTests.RunAll();
            EditRenderTests.RunAll();
            WindowReadTests.RunAll();
            RunJoinTests.RunAll();
            LatestWorkerTests.RunAll();
            BarrierPipelineTests.RunAll();
            RunCutTests.RunAll();
            ChainOverlapTests.RunAll();
            BatchTests.RunAll();
            MiniJsonTests.RunAll();
            RailChainTests.RunAll();
            RailSurfaceTests.RunAll();
            JunctionMouthTests.RunAll();
            LineEndsTests.RunAll();
            LineCutTests.RunAll();
            ContourTraceTests.RunAll();
            SignLineTests.RunAll();
            RoadGoneTests.RunAll();
            RoadRelationTests.RunAll();
            FollowFastTests.RunAll();
            FollowSchedulerTests.RunAll();
            FollowWatchTests.RunAll();
            FollowWriteTests.RunAll();
                FollowSpeedTests.RunAll();
                FollowPhaseTests.RunAll();
                ElevatedFollowTests.RunAll();
            PreferencesTests.RunAll();
            RailingTests.RunAll();
            MoreBarrierTests.RunAll();
            SaveRecordTests.RunAll();
            BeamRiseTests.RunAll();
            PaintTests.RunAll();
            AutoChoiceTests.RunAll();
            RoadStateTests.RunAll();
            LampAdTests.RunAll();
            GreeningTests.RunAll();
            FoliageTests.RunAll();
            BarrierLodTests.RunAll();
            AdaptiveTests.RunAll();
            SamplingTests.RunAll();
            ShadowWorkerTests.RunAll();
            PlantLedgerTests.RunAll();
            SurfaceCacheTests.RunAll();
            StyleProfileTests.RunAll();
            StyleChoiceTests.RunAll();
            TextLayoutTests.RunAll();
            TextGlyphTests.RunAll();
            PlannerPerfTests.RunAll();
            TextBoxTests.RunAll();
            TextFarTests.RunAll();
            StandPlannerTests.RunAll();
            StandBoxTests.RunAll();
            StandHeavyTests.RunAll();
            TextTiltTests.RunAll();
            TextFontTests.RunAll();
            TextStyleTests.RunAll();
            TextEffectTests.RunAll();
            TextArcTests.RunAll();
            TextJoinTests.RunAll();
            ClickReadTests.RunAll();
            MultiJunctionTests.RunAll();
            JunctionSequenceTests.RunAll();
            SpanSliderTests.RunAll();
            ClearanceTests.RunAll();
            TrimTests.RunAll();
            FacadeSamplingTests.RunAll();
            BuildingSurfaceTests.RunAll();
            UnicodeTests.RunAll();
            EmojiFontTests.RunAll();
            EmojiPlanTests.RunAll();
            LoadGateTests.RunAll();
            LoadPaceTests.RunAll();
            HoldBudgetTests.RunAll();
            EcsAuditTests.RunAll();
            ClearCensusTests.RunAll();
            ClearTraceTests.RunAll();
            ClearLateTests.RunAll();
            WarmUpTests.RunAll();
            GreenBeamTests.RunAll();
            TypeSpeedTests.RunAll();
            BilingualTests.RunAll();
            LanePickTests.RunAll();
            HoardingRegressionTests.RunAll();
            HoardingStyleTests.RunAll();
            CitySignTests.RunAll();
            RoadPostTests.RunAll();
            GeometryGateTests.RunAll();
            DepressionTests.RunAll();
            BoundaryPostTests.RunAll();
            SpaceGantryTests.RunAll();
            LimitFrameTests.RunAll();
            OverRoadTests.RunAll();
            OverHungTests.RunAll();
            // After the tests of the fonts: these two load the bundled font through LightingPrint.Fonts, which the text boxes' and the lamp ads' tests count on not being loaded yet.
            SurfaceNormalsTests.RunAll();
            PaintExperimentTests.RunAll();
            WallHeightTests.RunAll();
            FluorescentTests.RunAll();
            TurnAdjustTests.RunAll();
            DecalSettingsTests.RunAll();
            GameShaderTests.RunAll();
            TextBudgetTests.RunAll();
            NativeEnglishTests.RunAll();
            LightingTests.RunAll();
            return Summary();
        }

        private static int Summary()
        {
            Console.WriteLine();
            Console.WriteLine($"{s_Checks} checks, {s_Failures.Count} failed");
            foreach (string failure in s_Failures)
                Console.WriteLine("  FAIL " + failure);
            Console.WriteLine(s_Failures.Count == 0 ? "RESULT=PASS" : "RESULT=FAIL");
            return s_Failures.Count == 0 ? 0 : 1;
        }

        internal static void Run(string name, Action test)
        {
            try
            {
                test();
            }
            catch (Exception ex)
            {
                s_Failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void Check(bool ok, string message)
        {
            s_Checks++;
            if (!ok)
                s_Failures.Add(message);
        }

        internal static void Near(float actual, float expected, float tolerance, string message) =>
            Check(Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected}, got {actual}");

        // A road along +Z from the origin, 100 m long.
        private static ArcTable Straight() => new ArcTable(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f)));

        // A quarter circle of radius 50 about (50, 0, 0), from (0, 0, 0) heading +Z to (50, 0, 50) heading +X (a right-hand bend).
        private static ArcTable QuarterCircle()
        {
            const float r = 50f;
            const float k = 0.5522847f;
            return new ArcTable(new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, k * r), new Vec3(r - k * r, 0f, r), new Vec3(r, 0f, r)));
        }

        private static void StraightCurve()
        {
            Bezier3 line = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f));
            Near(line.Point(0.5f).Z, 50f, 0.01f, "midpoint");
            Near(line.Tangent(0.3f).HeadingXZ().Z, 1f, 1e-5f, "heading along +Z");
            Near(line.ClosestT(new Vec3(7f, 0f, 25f)), 0.25f, 1e-3f, "closest t");
            Near(line.ClosestT(new Vec3(7f, 0f, -20f)), 0f, 1e-3f, "before the start clamps to 0");
            Near(line.ClosestT(new Vec3(7f, 0f, 400f)), 1f, 1e-3f, "after the end clamps to 1");
            Vec3 right = line.Tangent(0.5f).RightXZ();
            Near(right.X, 1f, 1e-5f, "the right of +Z is +X (left-handed, Y up)");
        }

        private static void BentCurve()
        {
            ArcTable bend = QuarterCircle();
            // The point of the circle at 45 degrees, pushed outwards: its nearest point on the curve is at the middle.
            double a = Math.PI / 4;
            var outside = new Vec3(50f - 60f * (float)Math.Cos(a), 0f, 60f * (float)Math.Sin(a));
            float t = bend.Curve.ClosestT(outside);
            Vec3 p = bend.Curve.Point(t);
            Near(Vec3.DistanceXZ(p, new Vec3(50f, 0f, 0f)), 50f, 0.1f, "the nearest point lies on the circle");
            Near(t, 0.5f, 0.01f, "the middle of a symmetric bend");
        }

        private static void ArcLengths()
        {
            ArcTable straight = Straight();
            Near(straight.Length, 100f, 0.01f, "straight length");
            Near(straight.TAt(25f), 0.25f, 1e-3f, "t at a quarter of the length");
            Near(straight.SAt(0.75f), 75f, 0.01f, "s at three quarters");
            Near(straight.TAt(-5f), 0f, 0f, "negative distance clamps");
            Near(straight.TAt(500f), 1f, 0f, "excess distance clamps");

            ArcTable bend = QuarterCircle();
            Near(bend.Length, (float)(Math.PI * 50 / 2), 0.2f, "quarter circle length");
            for (float s = 0f; s <= bend.Length; s += 5f)
                Near(bend.SAt(bend.TAt(s)), s, 0.05f, $"round trip at {s}");
        }

        private static void CollapsedEnd()
        {
            // The first two control points coincide, so the derivative at 0 is zero: the heading must come from the chord.
            var curve = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 40f), new Vec3(0f, 0f, 100f));
            Vec3 h = curve.Tangent(0f).HeadingXZ();
            Near(h.Z, 1f, 1e-4f, "heading at a collapsed start");
            Check(h.IsFinite, "heading is finite");
        }

        private static void EdgeStraight()
        {
            // A road 8 m wide; the cursor 9 m to the right of the centre line.
            EdgeSnapResult r = EdgeSnap.Solve(Straight(), 8f, new Vec3(9f, 0f, 40f), EdgeSnapOptions.Default);
            Check(r.Side == 1, "the cursor is on the right");
            Near(r.T, 0.4f, 1e-3f, "t");
            Near(r.Distance, 40f, 0.05f, "distance along");
            Near(r.Lateral, 4.5f, 1e-4f, "half the width plus the margin");
            Near(r.Position.X, 4.5f, 1e-4f, "position x");
            Near(r.Position.Z, 40f, 0.05f, "position z");
            // Right-hand traffic on the right side comes along +Z, so the sign looks back at -Z: yaw 180 degrees.
            Near(r.Facing.Z, -1f, 1e-5f, "facing -Z");
            Near(Math.Abs(r.Yaw), (float)Math.PI, 1e-4f, "yaw is 180 degrees");
            Check(!r.TooFar, "not too far");

            EdgeSnapResult l = EdgeSnap.Solve(Straight(), 8f, new Vec3(-12f, 0f, 60f), EdgeSnapOptions.Default);
            Check(l.Side == -1, "the cursor is on the left");
            Near(l.Position.X, -4.5f, 1e-4f, "left position x");
            Near(l.Facing.Z, 1f, 1e-5f, "left side faces +Z (traffic there comes along -Z)");
            Near(l.Yaw, 0f, 1e-4f, "yaw 0");
        }

        private static void EdgeOptions()
        {
            var options = EdgeSnapOptions.Default;
            options.Side = SnapSide.Left;
            EdgeSnapResult forced = EdgeSnap.Solve(Straight(), 8f, new Vec3(30f, 0f, 10f), options);
            Check(forced.Side == -1, "the forced side wins over the cursor");
            Near(forced.Position.X, -4.5f, 1e-4f, "forced left x");

            options.Side = SnapSide.Auto;
            options.Margin = -1f;
            options.HeightOffset = 0.2f;
            EdgeSnapResult inside = EdgeSnap.Solve(Straight(), 8f, new Vec3(30f, 0f, 10f), options);
            Near(inside.Position.X, 3f, 1e-4f, "a negative margin goes inwards");
            Near(inside.Position.Y, 0.2f, 1e-5f, "height offset");

            options.Margin = 0f;
            EdgeSnapResult zero = EdgeSnap.Solve(Straight(), 0f, new Vec3(1f, 0f, 10f), options);
            Near(zero.Position.X, 0f, 1e-5f, "a road of no width is the centre line");
            EdgeSnapResult negative = EdgeSnap.Solve(Straight(), -5f, new Vec3(1f, 0f, 10f), options);
            Near(negative.Position.X, 0f, 1e-5f, "a negative width is taken as none");
        }

        private static void LeftHandTraffic()
        {
            var options = EdgeSnapOptions.Default;
            options.RightHandTraffic = false;
            EdgeSnapResult r = EdgeSnap.Solve(Straight(), 8f, new Vec3(9f, 0f, 40f), options);
            Near(r.Facing.Z, 1f, 1e-5f, "right side with left-hand traffic faces +Z");
        }

        private static void EdgeBend()
        {
            ArcTable bend = QuarterCircle();
            // Outside the bend at 45 degrees, 10 m beyond a road 8 m wide: the item stands 4.5 m outside the centre line.
            double a = Math.PI / 4;
            var cursor = new Vec3(50f - 60f * (float)Math.Cos(a), 0f, 60f * (float)Math.Sin(a));
            var options = EdgeSnapOptions.Default;
            EdgeSnapResult r = EdgeSnap.Solve(bend, 8f, cursor, options);
            // The cursor is on the left of the heading (the bend turns right, its outside is on the left).
            Check(r.Side == -1, "outside of a right-hand bend is the left side");
            Near(Vec3.DistanceXZ(r.Position, new Vec3(50f, 0f, 0f)), 54.5f, 0.15f, "4.5 m outside the circle");
            Near(Vec3.Dot(r.Heading, r.Facing), 1f, 1e-4f, "left side faces along the heading");
        }

        private static void EdgeFar()
        {
            EdgeSnapResult r = EdgeSnap.Solve(Straight(), 8f, new Vec3(300f, 0f, 40f), EdgeSnapOptions.Default);
            Check(r.TooFar, "300 m from the road is too far");
            EdgeSnapResult near = EdgeSnap.Solve(Straight(), 8f, new Vec3(30f, 0f, 40f), EdgeSnapOptions.Default);
            Check(!near.TooFar, "30 m from the road is within reach");
        }

        private static void ApproachSetback()
        {
            // An approach 100 m long heading +Z into a junction at its end.
            ApproachPose p = Approach.BeforeEnd(Straight(), 2.5f, 1.75f);
            Near(p.Position.Z, 97.5f, 0.05f, "2.5 m before the end");
            Near(p.Position.X, 1.75f, 1e-4f, "1.75 m to the right of the centre line");
            Near(p.DistanceFromEnd, 2.5f, 0.05f, "recorded setback");
            Near(p.Yaw, 0f, 1e-4f, "faces the direction of travel");

            ApproachPose past = Approach.BeforeEnd(Straight(), 500f, 0f);
            Near(past.Position.Z, 0f, 0.05f, "a setback longer than the approach is the start");
        }

        private static void DashPattern()
        {
            // The centre line of a road open to overtaking: 4 m of line and 6 m of gap.
            List<Dash> dashes = Marking.Dashes(100f, 4f, 6f);
            Check(dashes.Count == 10, $"ten dashes in 100 m, got {dashes.Count}");
            Near(dashes[0].Start, 0f, 1e-5f, "first dash start");
            Near(dashes[0].End, 4f, 1e-5f, "first dash end");
            Near(dashes[9].Start, 90f, 1e-4f, "last dash start");
            Near(dashes[9].End, 94f, 1e-4f, "last dash end");

            List<Dash> cut = Marking.Dashes(12f, 4f, 6f);
            Check(cut.Count == 2, "two dashes in 12 m");
            Near(cut[1].End, 12f, 1e-5f, "the second dash is cut by the end of the line");
        }

        private static void DashEdgeCases()
        {
            List<Dash> shifted = Marking.Dashes(30f, 4f, 6f, phase: 3f);
            Near(shifted[0].Start, 0f, 1e-5f, "a dash running at the start begins at 0");
            Near(shifted[0].End, 1f, 1e-4f, "with 3 m of the pattern gone, 1 m of the dash is left");
            Near(shifted[1].Start, 7f, 1e-4f, "the next dash");

            List<Dash> negative = Marking.Dashes(30f, 4f, 6f, phase: -2f);
            Near(negative[0].End, 6f, 1e-4f, "a negative phase moves the other way");

            List<Dash> solid = Marking.Dashes(30f, 4f, 0f);
            Check(solid.Count == 1 && Math.Abs(solid[0].End - 30f) < 1e-5f, "no gap is one solid line");
            Check(Marking.Dashes(0f, 4f, 6f).Count == 0, "no length, no dashes");
            Check(Marking.Dashes(30f, 0f, 6f).Count == 0, "no dash length, no dashes");
            Check(Marking.Dashes(30f, 4f, 6f, phase: 1000003f).Count > 0, "a huge phase still gives dashes");

            List<Dash> sliver = Marking.Dashes(10.02f, 4f, 6f, minDash: 0.05f);
            Check(sliver.Count == 1, "a 2 cm remnant at the end is dropped");
        }

        private static void OffsetOnBend()
        {
            ArcTable bend = QuarterCircle();
            // 3.5 m to the right of the bend's centre line is 3.5 m nearer the centre of the circle.
            List<Vec3> line = Marking.Offset(bend, 5f, 60f, 3.5f, 1.5f);
            // 55 m in steps of at most 1.5 m is 37 segments, 38 points.
            Check(line.Count == 38, $"37 segments on a 55 m stretch, got {line.Count - 1}");
            foreach (Vec3 p in line)
                Near(Vec3.DistanceXZ(p, new Vec3(50f, 0f, 0f)), 46.5f, 0.12f, "distance to the circle centre");

            for (int i = 1; i < line.Count; i++)
                Check(Vec3.DistanceXZ(line[i], line[i - 1]) <= 1.7f, $"points {i - 1} and {i} are no more than a step apart");

            Check(Marking.Offset(bend, 60f, 5f, 3.5f).Count == 0, "a reversed range is empty");
            Check(Marking.Offset(bend, 1000f, 2000f, 3.5f).Count == 0, "a range past the end is empty");
        }
    }
}
