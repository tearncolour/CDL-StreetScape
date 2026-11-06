using System;
using System.Reflection;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>HintInfo equality (0.7.1): the panel's line of help is made again only when the hint changed, so every field that the text depends on must be part of the comparison.</summary>
    internal static class HintEqualityTests
    {
        public static void RunAll()
        {
            Program.Run("hint equality: every field is compared", EveryField);
            Program.Run("hint equality: the same hint is equal and the default is equal to itself", Same);
        }

        private static void Same()
        {
            HintInfo a = default, b = default;
            Program.Check(a.Equals(b) && a.Equals((object)b), "the default is equal to the default");
            var x = new HintInfo { Kind = "attach", Count = 3, Left = 1.25f, Need = 0.8f, X = 12.5f, Max = 4, Span = 14f, Grew = true, Manual = true, Hung = true, Bad = false, Name = "示1", Host = "lamp" };
            var y = new HintInfo { Kind = new string("attach".ToCharArray()), Count = 3, Left = 1.25f, Need = 0.8f, X = 12.5f, Max = 4, Span = 14f, Grew = true, Manual = true, Hung = true, Bad = false, Name = new string("示1".ToCharArray()), Host = "lamp" };
            Program.Check(x.Equals(y) && x.GetHashCode() == y.GetHashCode(), "the same fields in other string objects are equal, with the same hash");
            Program.Check(!x.Equals(a) && !a.Equals(x) && !x.Equals((object)"not a hint") && !x.Equals(null), "and not equal to others");
            Program.Check(new HintInfo { Kind = null }.Equals(new HintInfo { Kind = null }) && !new HintInfo { Kind = null }.Equals(new HintInfo { Kind = "" }), "a null kind is a kind of its own (the texts treat both as nothing, but the panel is told only on a change)");
        }

        private static void EveryField()
        {
            var baseline = new HintInfo { Kind = "attach", Count = 3, Left = 1.25f, Need = 0.8f, X = 12.5f, Max = 4, Span = 14f, Grew = false, Manual = false, Hung = false, Bad = false, Name = "示1", Host = "lamp" };
            int fields = 0;
            foreach (FieldInfo field in typeof(HintInfo).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                fields++;
                object boxed = baseline;
                object original = field.GetValue(boxed);
                object changed;
                if (field.FieldType == typeof(string))
                    changed = original + "x";
                else if (field.FieldType == typeof(int))
                    changed = (int)original + 1;
                else if (field.FieldType == typeof(float))
                    changed = (float)original + 1f;
                else if (field.FieldType == typeof(bool))
                    changed = !(bool)original;
                else if (field.FieldType.IsEnum)
                    changed = Enum.ToObject(field.FieldType, Convert.ToInt32(original) + 1);
                else
                    throw new InvalidOperationException("a field of a type the test does not know: " + field.Name + " " + field.FieldType);
                field.SetValue(boxed, changed);
                var other = (HintInfo)boxed;
                Program.Check(!baseline.Equals(other) && !other.Equals(baseline), "a change of " + field.Name + " is a different hint");
            }

            Program.Check(fields >= 14, "the test went over every field of HintInfo (" + fields + "): a new field is compared too, or this fails");
        }
    }
}
