using System;
using Xunit;
using DshLauncher;

namespace DshLauncher.Tests
{
    /// <summary>
    /// PortableNode.ParseShaLine 测试：从 nodejs.org SHASUMS256.txt 文本中
    /// 按文件名查找对应的 SHA256。
    /// </summary>
    public class ShaLineParserTests
    {
        // 真实 SHASUMS256.txt 头部样本
        private const string SampleSums = @"f4e35c13165de6880caa2558c0aa48ca88ada47fe2234bed07d66dbb80a47c8d  node-v24.19.0-aix-ppc64.tar.gz
47b16e1b1012b1b9ad62169b3a466adb6bc758b2cb8bd8224683c086836484f8  node-v24.19.0-arm64.msi
57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73  node-v24.19.0-win-x64.zip
d1b5e999db158c62fe8f7267a4476b035d8bd93b1a605bac24a3f0dd166e3316  node-v24.19.0-darwin-x64.tar.gz
";

        [Fact]
        public void FindsExactZip()
        {
            string sha = PortableNode.ParseShaLine(SampleSums, "node-v24.19.0-win-x64.zip");
            Assert.Equal("57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73", sha);
        }

        [Fact]
        public void CaseInsensitiveFilename()
        {
            // Windows 文件名大小写不敏感
            string sha = PortableNode.ParseShaLine(SampleSums, "NODE-V24.19.0-WIN-X64.ZIP");
            Assert.Equal("57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73", sha);
        }

        [Fact]
        public void ExecutableMarker_StarPrefix_Stripped()
        {
            // nodejs.org SHASUMS256.txt 里 * 前缀表示"在 win 安装包里"
            string sums = "abc123" + new string('a', 58) + "  *node-v20.0.0-win-x64.zip";
            string sha = PortableNode.ParseShaLine(sums, "node-v20.0.0-win-x64.zip");
            Assert.NotNull(sha);
            Assert.Equal(64, sha.Length);
        }

        [Fact]
        public void NotFound_ReturnsNull()
        {
            Assert.Null(PortableNode.ParseShaLine(SampleSums, "node-v99.99.99-win-x64.zip"));
        }

        [Fact]
        public void NullInputs_ReturnNull()
        {
            Assert.Null(PortableNode.ParseShaLine(null, "x.zip"));
            Assert.Null(PortableNode.ParseShaLine(SampleSums, null));
            Assert.Null(PortableNode.ParseShaLine(null, null));
        }

        [Fact]
        public void MalformedLines_Skipped()
        {
            // 短 hash、空格位置不对、过长等都应跳过而非抛
            string messy = "short  badline\n" + SampleSums + "\n\n";
            string sha = PortableNode.ParseShaLine(messy, "node-v24.19.0-win-x64.zip");
            Assert.Equal("57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73", sha);
        }

        [Fact]
        public void EmptyText_ReturnsNull()
        {
            Assert.Null(PortableNode.ParseShaLine("", "node-v24.19.0-win-x64.zip"));
        }

        [Fact]
        public void LfOnlyLineEndings_Work()
        {
            // 单 LF 而非 CRLF
            string lf = SampleSums.Replace("\r\n", "\n");
            Assert.Equal("57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73",
                PortableNode.ParseShaLine(lf, "node-v24.19.0-win-x64.zip"));
        }
    }
}
