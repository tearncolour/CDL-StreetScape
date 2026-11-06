using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.106 (the owner, 7 Nov: "RIE中移动灯杆位置之后竖杆挂上去的路牌不会跟随" and "确保我不订阅RIE的情况下对应的部分不会出现"): the boards on another mod's pole or arm go with it (OtherHosts: which host
    /// a board is on, where the host is now, whether it moved), and the support on another mod's arm is offered only while some mod gives arms (SupportCatalog.HostsProvided).
    /// </summary>
    internal static class OtherHostFollowTests
    {
        public static void RunAll()
        {
            Program.Run("other host follow: the post a board stands at the foot of, and the arm it is on", WhichHost);
            Program.Run("other host follow: a host's pose and number as the lists give them", Poses);
            Program.Run("other host follow: a pole that moved or turned takes its boards the same way, an unmoved one does not", FollowsThePole);
            Program.Run("other host follow: the support on another mod's arm is offered only while a mod gives arms", ArmSupportOffered);
        }

        private static HostCandidate Post(float x, float y, float z, int id) => new HostCandidate { X = x, Y = y, Z = z, Kind = HostCandidate.Signal, Id = id, Radius = 0.065f };

        private static HostArm Arm(float x, float y, float z, float dirX, float dirZ, int id) => new HostArm { X = x, Y = y, Z = z, DirX = dirX, DirZ = dirZ, Length = 8f, Radius = 0.12f, Id = id };

        private static void WhichHost()
        {
            var posts = new List<HostCandidate> { Post(10f, 0f, 10f, 5), Post(11f, 0f, 10f, 6), Post(30f, 0f, 30f, 7) };
            Program.Check(OtherHosts.PostUnder(posts, new Vec3(10f, 0f, 10.2f)) == 0, "the post a board stands at the foot of");
            Program.Check(OtherHosts.PostUnder(posts, new Vec3(10.9f, 0f, 10f)) == 1, "the nearer of two");
            Program.Check(OtherHosts.PostUnder(posts, new Vec3(10.5f, 0f, 10f)) == 0, "equally near: the earlier, so that the choice does not flicker");
            Program.Check(OtherHosts.PostUnder(posts, new Vec3(10f, 0f, 12f)) == -1, "more than HostPick.Fixed from every post: none");
            Program.Check(OtherHosts.PostUnder(posts, new Vec3(10f, 2.4f, 10f)) == 0 && OtherHosts.PostUnder(posts, new Vec3(10f, 2.6f, 10f)) == -1, "a foot 2.5 m below or above is another road's (a bridge's)");
            Program.Check(OtherHosts.PostUnder(new List<HostCandidate>(), new Vec3(1f, 0f, 1f)) == -1, "no posts: none");

            // An arm from (0, 6.2, 0) toward +X, and another across it toward +Z.
            var arms = new List<HostArm> { Arm(0f, 6.2f, 0f, 1f, 0f, 5), Arm(0f, 6.2f, 0f, 0f, 1f, 5) };
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(3f, 6.2f, 0.1f)) == 0, "a board on the first arm, 3 m out");
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(0.1f, 6.2f, 4f)) == 1, "a board on the second");
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(3f, 6.6f, 0f)) == -1, "0.4 m above the axis: not on it");
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(8.4f, 6.2f, 0f)) == 0 && OtherHosts.ArmUnder(arms, new Vec3(8.6f, 6.2f, 0f)) == -1, "past the tip by half a metre: still on it, further: not");
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(3f, 6.2f, 0.6f)) == -1, "0.6 m to the side: not on it");
            Program.Check(OtherHosts.ArmUnder(new List<HostArm>(), new Vec3(3f, 6.2f, 0f)) == -1, "no arms: none");
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(0.3f, 6.2f, 0.1f)) == 0 && OtherHosts.ArmUnder(arms, new Vec3(0.1f, 6.2f, 0.3f)) == 1, "near the root, where both arms qualify: the one whose axis is nearer");
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(-0.4f, 6.2f, 0f)) == 0 && OtherHosts.ArmUnder(arms, new Vec3(-0.6f, 6.2f, 0f)) == -1, "half a metre behind the root: still on it, further: not");
            Program.Check(OtherHosts.ArmUnder(arms, new Vec3(3f, 6.2f, 0.4f)) == 0, "0.4 m to the side: on it");
        }

        private static void Poses()
        {
            Vec3 foot = OtherHosts.PostPose(Post(1f, 2f, 3f, 5), out float postYaw);
            Program.Check(foot.X == 1f && foot.Y == 2f && foot.Z == 3f && postYaw == 0f, "a post: its foot, and no yaw");
            Vec3 root = OtherHosts.ArmPose(Arm(4f, 6f, 7f, 1f, 0f, 5), out float east);
            Program.Check(root.X == 4f && root.Y == 6f && root.Z == 7f, "an arm: its root at the height of its axis");
            Program.Near(east, (float)(Math.PI / 2.0), 1e-5f, "toward +X: a quarter turn, as the game's Transform gives a yaw");
            OtherHosts.ArmPose(Arm(0f, 0f, 0f, 0f, 1f, 5), out float north);
            Program.Near(north, 0f, 1e-5f, "toward +Z: no turn");

            var posts = new List<HostCandidate> { Post(1f, 0f, 1f, 5), Post(2f, 0f, 2f, 9) };
            var arms = new List<HostArm> { Arm(3f, 6f, 3f, 0f, 1f, 5) };
            Program.Check(OtherHosts.Find(posts, arms, false, 9, out Vec3 at, out _) && at.X == 2f, "a post known by its number");
            Program.Check(OtherHosts.Find(posts, arms, true, 5, out at, out _) && at.X == 3f && at.Y == 6f, "an arm of the number a post has too: the arm's, in the list of arms");
            Program.Check(!OtherHosts.Find(posts, arms, true, 9, out _, out _), "no arm of the number a post has: not found");
            Program.Check(!OtherHosts.Find(posts, arms, false, 77, out _, out _) && !OtherHosts.Find(posts, arms, false, -1, out _, out _), "a number that is not there, or not known: not found");
            var unnumbered = new List<HostCandidate> { Post(1f, 0f, 1f, -1) };
            Program.Check(!OtherHosts.Find(unnumbered, arms, false, -1, out _, out _), "a pole with no number (-1) is not found by it: it cannot be told from another");
            Program.Check(!OtherHosts.Find(new List<HostCandidate> { Post(float.NaN, 0f, 0f, 5) }, arms, false, 5, out _, out _), "a pose that is not a number: not found");
            Program.Check(!OtherHosts.Find(posts, new List<HostArm> { Arm(float.NaN, 6f, 3f, 0f, 1f, 5) }, true, 5, out _, out _) && !OtherHosts.Find(posts, new List<HostArm> { Arm(3f, 6f, 3f, float.NaN, 1f, 5) }, true, 5, out _, out _), "an arm whose root or direction is not a number: not found");

            Program.Check(!OtherHosts.Moved(new Vec3(1f, 2f, 3f), 0.5f, new Vec3(1.005f, 2f, 3f), 0.5f), "half a centimetre: not moved");
            Program.Check(OtherHosts.Moved(new Vec3(1f, 2f, 3f), 0.5f, new Vec3(1.02f, 2f, 3f), 0.5f), "two centimetres: moved");
            Program.Check(OtherHosts.Moved(new Vec3(1f, 2f, 3f), 0.5f, new Vec3(1f, 2.05f, 3f), 0.5f), "up the slope: moved");
            Program.Check(!OtherHosts.Moved(new Vec3(1f, 2f, 3f), 0.5f, new Vec3(1f, 2f, 3f), 0.501f) && OtherHosts.Moved(new Vec3(1f, 2f, 3f), 0.5f, new Vec3(1f, 2f, 3f), 0.505f), "six hundredths of a degree is not a turn, three tenths is");
            float pi = (float)Math.PI;
            Program.Check(!OtherHosts.Moved(new Vec3(1f, 2f, 3f), pi - 0.0005f, new Vec3(1f, 2f, 3f), -pi + 0.0005f), "across the wrap at half a turn: the same way, not moved");
        }

        /// <summary>One board on an arm and one on the post of a pole, moved with the pole by the follow as the system does it: the lists tell where the pole is, the follow takes the boards.</summary>
        private static void FollowsThePole()
        {
            var watch = new FollowWatch<int, int> { Writing = true };
            var poles = new Dictionary<int, (bool Arm, int Id)> { [1] = (true, 5), [2] = (false, 5) };
            var items = new Dictionary<int, (Vec3 At, float Yaw)> { [10] = (new Vec3(3f, 6.2f, 0f), 0f), [11] = (new Vec3(0f, 0f, 0f), 0f) };
            var kept = new Dictionary<int, (Vec3 At, float Yaw)>();
            var posts = new List<HostCandidate> { Post(0f, 0f, 0f, 5) };
            var arms = new List<HostArm> { Arm(0f, 6.2f, 0f, 1f, 0f, 5) };

            // Linked as the system links them: the board to what the lists give at its place.
            foreach (var board in new[] { (Item: 10, Arm: true), (Item: 11, Arm: false) })
            {
                Vec3 at = items[board.Item].At;
                int pick = board.Arm ? OtherHosts.ArmUnder(arms, at) : OtherHosts.PostUnder(posts, at);
                Program.Check(pick == 0, "board " + board.Item + " is on its host");
                Vec3 pose = board.Arm ? OtherHosts.ArmPose(arms[pick], out float yaw) : OtherHosts.PostPose(posts[pick], out yaw);
                int key = board.Arm ? 1 : 2;
                kept[key] = (pose, yaw);
                Program.Check(watch.LinkHosted(board.Item, key, pose, yaw, at, 0f), "linked board " + board.Item);
            }

            bool Pose(int host, out Vec3 at, out float yaw)
            {
                at = kept[host].At;
                yaw = kept[host].Yaw;
                return true;
            }

            bool Place(int item, bool wantPath, out Vec3 at, out float yaw, out string path)
            {
                at = items[item].At;
                yaw = items[item].Yaw;
                path = null;
                return true;
            }

            int Step(ref int frame)
            {
                var writes = new List<FollowWrite<int>>();
                watch.StepWrite(edge => null, Place, FollowMode.Fraction, frame++, 5e8, 200, writes, Pose);
                foreach (FollowWrite<int> write in writes)
                    items[write.Item] = (write.Position, write.Yaw);
                return writes.Count;
            }

            // The poll: the pole is asked for by its number where it was; it is where the lists now put it. An unmoved pole is not told.
            int frameNumber = 0;
            Program.Check(OtherHosts.Find(posts, arms, true, 5, out Vec3 stillAt, out float stillYaw) && !OtherHosts.Moved(kept[1].At, kept[1].Yaw, stillAt, stillYaw), "an unmoved pole is not told to the follow");
            Program.Check(Step(ref frameNumber) == 0, "and nothing is written");

            // The pole is moved 12 m east and 5 m south and turned a quarter: the post's board goes by the move, the arm's by the move and the turn about the pole's axis.
            posts[0] = Post(12f, 0.5f, -5f, 5);
            arms[0] = Arm(12f, 6.7f, -5f, 0f, -1f, 5);
            foreach (int key in new[] { 1, 2 })
            {
                Program.Check(OtherHosts.Find(posts, arms, poles[key].Arm, poles[key].Id, out Vec3 now, out float yaw), "the pole is found by its number, " + (poles[key].Arm ? "its arm" : "its post"));
                Program.Check(OtherHosts.Moved(kept[key].At, kept[key].Yaw, now, yaw), "and has moved");
                kept[key] = (now, yaw);
                watch.Dirty(key);
            }

            int guard = 0, written = 0;
            while (guard++ < 10)
                written += Step(ref frameNumber);
            Program.Check(written == 2, "both boards were written: " + written);
            Vec3 board11 = items[11].At, board10 = items[10].At;
            Program.Check(Math.Abs(board11.X - 12f) < 1e-3f && Math.Abs(board11.Y - 0.5f) < 1e-3f && Math.Abs(board11.Z + 5f) < 1e-3f, $"the board at the post's foot is at the post's foot again: {board11.X:0.00}, {board11.Y:0.00}, {board11.Z:0.00}");
            // Its arm pointed east and now points south (a quarter turn clockwise seen from above): a board 3 m out on it is 3 m south of the pole, half a metre higher.
            Program.Check(Math.Abs(board10.X - 12f) < 1e-3f && Math.Abs(board10.Y - 6.7f) < 1e-3f && Math.Abs(board10.Z + 8f) < 1e-3f, $"the board on the arm is still 3 m out on it, on the axis: {board10.X:0.00}, {board10.Y:0.00}, {board10.Z:0.00}");
            Program.Check(HostPick.AlongArm(arms[0], board10) > 2.99f && HostPick.AlongArm(arms[0], board10) < 3.01f, "and is on the arm as HostPick finds it");
            Program.Near(items[10].Yaw, (float)(Math.PI / 2.0), 1e-3f, "its facing turned with the arm");
        }

        private static void ArmSupportOffered()
        {
            var registry = new ExtensionRegistry<int>();
            Program.Check(!registry.ProvidesHosts, "no extension registered: no poles given");
            registry.Add(new ExtensionRegistry<int>.Hooks { Name = "a" });
            Program.Check(!registry.ProvidesHosts, "an extension with no hosts of its own: none given");
            registry.Add(new ExtensionRegistry<int>.Hooks { Name = "b", OtherHosts = (x, z, radius, posts, arms) => { } });
            Program.Check(registry.ProvidesHosts, "an extension that gives them: poles given");

            Func<bool> was = SupportCatalog.HostsProvided;
            try
            {
                SupportDef arm = SupportCatalog.ById(SupportCatalog.AttachedArm);
                Program.Check(arm != null && SupportKinds.OnArm(arm.Kind) && !arm.Hidden, "the support on a signal arm is a support of the catalogue, not hidden");
                SupportCatalog.HostsProvided = () => false;
                Program.Check(!SupportCatalog.Listed.Contains(arm), "no mod gives arms: it is not offered");
                Program.Check(SupportCatalog.Listed.Count == SupportCatalog.Offerable.Count - 1, "and it is the only support not offered");
                Program.Check(SupportCatalog.Listed.Contains(SupportCatalog.ById(SupportCatalog.AttachedPole)), "a board on the game's own lamp post or signal pole is offered");
                Program.Check(SupportCatalog.ByHash(arm.Hash) == arm && SupportCatalog.All.Contains(arm), "a board saved on an arm still finds its support by its hash");
                Program.Check(SupportCatalog.Offerable.Contains(arm), "its picture is made at the start, for when the mod joins");
                SupportCatalog.HostsProvided = () => true;
                Program.Check(SupportCatalog.Listed.Contains(arm) && SupportCatalog.Listed.Count == SupportCatalog.Offerable.Count, "a mod gives arms: it is offered, as it was");
                Program.Check(SupportCatalog.HostsProvided != null && SupportCatalog.Listed.Count > 0, "the switch is read at each look: no state is kept");
            }
            finally
            {
                SupportCatalog.HostsProvided = was;
            }

            Program.Check(!SupportCatalog.HostsProvided(), "the catalogue starts without arms: nothing joined");
        }
    }
}
