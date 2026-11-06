using System;
using CDLC.TS.Geo;

namespace CDLC.TS
{
    /// <summary>
    /// The batch interface of CDL:StreetScape (0.12.67), for tools that place many boards at once (the first is the interchange designer, whose contract is DESIGN-solver.md 7): they find this type
    /// by its name in the main assembly (Type.GetType("CDLC.TS.TsBatchApi, CDLC.TS")) and call the three members with strings; JSON in and out, no shared types, main thread only.
    /// The request is {"v":1,"batch":"id","items":[{"i":0,"catalog":"gb.exit.adv.2km","fields":{"to1":"..."},"support":"","x":0,"y":0,"z":0,"yaw":0}]} (yaw in radians about Y, as TsItem.Yaw;
    /// "mainStation" is accepted and not used: the road an item stands beside is found by its place). Submit answers at once and the work goes on over the next frames: it never claims what is not
    /// known yet (no "unmatched" before Status says the batch is written). The members, the answers' fields and the rules are in Geo/BatchRequest.cs and Geo/BatchBook.cs; the work is TsBatchSystem.
    /// </summary>
    public static class TsBatchApi
    {
        /// <summary>
        /// Queues a batch (replacing the items of an earlier batch of the same id when it is written). The answer is {"batch", "queued", "items":[{"i","ignoredKeys":[...]}], "rejected":[{"i","reason"}]},
        /// or {"batch", "error"} when the whole batch is refused (bad JSON, another version, more than 300 items, no known catalogue id, no city loaded): nothing is written then.
        /// </summary>
        public static string Submit(string json)
        {
            try
            {
                TsBatchSystem system = TsBatchSystem.Instance;
                if (system != null)
                    return system.Submit(json);
                BatchPlan plan = BatchRequest.Parse(json);
                if (plan.Error.Length == 0)
                    plan.Error = "the batch interface is not running";
                return BatchBook.SubmitJson(plan);
            }
            catch (Exception ex)
            {
                return Failed(ex);
            }
        }

        /// <summary>What a catalogue id takes: its fields (key, type, default, limits, choices), the preset's values and the supports a batch may name; {"catalog", "error"} for an id that is no board.</summary>
        public static string Describe(string catalogId)
        {
            try
            {
                return BatchRequest.Describe(catalogId);
            }
            catch (Exception ex)
            {
                return Failed(ex);
            }
        }

        /// <summary>
        /// How a batch is getting on: {"batch", "state": queued | preparing | written | failed, "queued", "written", "items":[{"i","ignoredKeys"}], "rejected":[...]} and, once the state is written or
        /// failed only, "unmatched": the numbers of the items that stood beside no road and were not written. A batch this session does not know is failed with "error": "unknown batch".
        /// </summary>
        public static string Status(string batch)
        {
            try
            {
                TsBatchSystem system = TsBatchSystem.Instance;
                return system != null ? system.Status(batch) : BatchBook.StatusJson(batch, null);
            }
            catch (Exception ex)
            {
                return Failed(ex);
            }
        }

        private static string Failed(Exception ex)
        {
            Mod.log.Error("TsBatchApi: " + ex);
            return new JsonWriter().BeginObject().Key("error").String(ex.GetType().Name + ": " + ex.Message).EndObject().ToString();
        }
    }
}
