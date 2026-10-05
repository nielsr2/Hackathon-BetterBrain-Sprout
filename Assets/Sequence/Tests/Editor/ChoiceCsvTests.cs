using System;
using NUnit.Framework;

namespace Sequence.Tests
{
    public class ChoiceCsvTests
    {
        [Test]
        public void Parse_ReadsLabelsAndVideos()
        {
            var o = ChoiceCsv.Parse("button,video\nSTAY,stay.mp4\nRUN,run.mp4\n", 2, out var warnings);
            Assert.AreEqual(2, o.Count);
            Assert.AreEqual("STAY", o[0].Label);
            Assert.AreEqual("stay.mp4", o[0].Video);
            Assert.AreEqual("RUN", o[1].Label);
            Assert.AreEqual("run.mp4", o[1].Video);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Parse_HeaderOrderCaseAndExtraColumns()
        {
            var o = ChoiceCsv.Parse("Video , notes, BUTTON\r\na.mp4,x,A\r\nb.mp4,y,B", 2, out _);
            Assert.AreEqual("A", o[0].Label);
            Assert.AreEqual("b.mp4", o[1].Video);
        }

        [Test]
        public void Parse_QuotedFieldsWithCommasAndQuotes()
        {
            var o = ChoiceCsv.Parse("button,video\n\"GO, NOW\",go.mp4\n\"SAY \"\"HI\"\"\",\"sub dir/hi.mp4\"", 2, out _);
            Assert.AreEqual("GO, NOW", o[0].Label);
            Assert.AreEqual("SAY \"HI\"", o[1].Label);
            Assert.AreEqual("sub dir/hi.mp4", o[1].Video);
        }

        [Test]
        public void Parse_SkipsBlankAndCommentLines()
        {
            var o = ChoiceCsv.Parse("# choices\n\nbutton,video\n\nA,a.mp4\n# B is next\nB,b.mp4\n\n", 2, out _);
            Assert.AreEqual("B", o[1].Label);
        }

        [Test]
        public void Parse_ExtraRowsAreDroppedWithAWarning()
        {
            var o = ChoiceCsv.Parse("button,video\nA,a.mp4\nB,b.mp4\nC,c.mp4", 2, out var warnings);
            Assert.AreEqual(2, o.Count);
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void Parse_RejectsBadInput()
        {
            Assert.Throws<FormatException>(() => ChoiceCsv.Parse("", 2, out _), "empty");
            Assert.Throws<FormatException>(() => ChoiceCsv.Parse("label,file\nA,a.mp4\nB,b.mp4", 2, out _), "missing columns");
            Assert.Throws<FormatException>(() => ChoiceCsv.Parse("button,video\nA,a.mp4", 2, out _), "too few rows");
            Assert.Throws<FormatException>(() => ChoiceCsv.Parse("button,video\nA,\nB,b.mp4", 2, out _), "empty video");
            Assert.Throws<FormatException>(() => ChoiceCsv.Parse("button,video\n\"A,a.mp4\nB,b.mp4", 2, out _), "unclosed quote");
        }
    }
}
