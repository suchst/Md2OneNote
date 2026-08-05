using System;
using System.Collections.Generic;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    public class AssetPathPolicyTests
    {
        private const string Base = @"C:\notes\project";

        private readonly AssetPathPolicy _policy = new AssetPathPolicy(TestCatalog.Instance);

        private AssetPathDecision Evaluate(string rawPath, AssetPolicy policy = null)
        {
            return _policy.Evaluate(rawPath, Base, policy ?? AssetPolicy.Default);
        }

        [Theory]
        [InlineData("logo.png", @"C:\notes\project\logo.png")]
        [InlineData("images/logo.png", @"C:\notes\project\images\logo.png")]
        [InlineData(@"images\logo.png", @"C:\notes\project\images\logo.png")]
        [InlineData("./images/logo.png", @"C:\notes\project\images\logo.png")]
        [InlineData("images/../logo.jpg", @"C:\notes\project\logo.jpg")]
        [InlineData("my%20picture.png", @"C:\notes\project\my picture.png")]
        public void A_relative_path_under_the_document_is_allowed_without_comment(string raw, string expected)
        {
            var decision = Evaluate(raw);

            Assert.True(decision.IsAllowed);
            Assert.Equal(expected, decision.FullPath);
            Assert.Null(decision.Code);
        }

        [Theory]
        [InlineData("../shared/logo.png")]
        [InlineData("..%2Fshared%2Flogo.png")]
        public void Escaping_the_document_folder_is_allowed_but_reported(string raw)
        {
            // A shared-image folder beside the document is a real layout, and the dangerous forms
            // are refused regardless of this. Encoded and plain traversal must land identically.
            var decision = Evaluate(raw);

            Assert.True(decision.IsAllowed);
            Assert.Equal(@"C:\notes\shared\logo.png", decision.FullPath);
            Assert.Equal(AssetPathCodes.OutsideDocument, decision.Code);
            Assert.NotNull(decision.Message);
        }

        [Fact]
        public void Escaping_can_be_refused_outright_when_the_policy_says_so()
        {
            var strict = new AssetPolicy(1024, false, AssetPolicy.Default.AllowedExtensions);

            var decision = Evaluate("../shared/logo.png", strict);

            Assert.False(decision.IsAllowed);
            Assert.Equal(AssetPathCodes.OutsideDocument, decision.Code);
            Assert.Null(decision.FullPath);
        }

        [Theory]
        [InlineData(@"\\server\share\logo.png")]
        [InlineData("//server/share/logo.png")]
        [InlineData(@"\\?\C:\notes\project\logo.png")]
        [InlineData(@"\\.\PhysicalDrive0\logo.png")]
        [InlineData("%5C%5Cserver%5Cshare%5Clogo.png")]
        public void A_network_or_device_path_is_always_refused(string raw)
        {
            // Reading one authenticates this machine to whoever answers. No setting turns it on.
            var decision = Evaluate(raw);

            Assert.False(decision.IsAllowed);
            Assert.Equal(AssetPathCodes.NetworkPath, decision.Code);
        }

        [Fact]
        public void A_network_path_is_refused_even_when_escaping_is_permitted()
        {
            var permissive = new AssetPolicy(1024, true, AssetPolicy.Default.AllowedExtensions);

            Assert.False(Evaluate(@"\\server\share\logo.png", permissive).IsAllowed);
        }

        [Theory]
        [InlineData(@"C:\Users\someone\.ssh\id_rsa.png")]
        [InlineData(@"D:\secrets\logo.png")]
        [InlineData("/etc/passwd.png")]
        [InlineData(@"\notes\logo.png")]
        [InlineData("C:logo.png")]
        public void An_absolute_or_drive_relative_path_is_refused(string raw)
        {
            var decision = Evaluate(raw);

            Assert.False(decision.IsAllowed);
            Assert.Contains(decision.Code, new[] { AssetPathCodes.AbsolutePath, AssetPathCodes.NetworkPath });
        }

        [Theory]
        [InlineData("https://example.com/logo.png")]
        [InlineData("http://example.com/logo.png")]
        [InlineData("data:image/png;base64,iVBORw0KGgo=")]
        [InlineData("file:///C:/notes/logo.png")]
        [InlineData("ftp://example.com/logo.png")]
        public void A_web_address_is_refused_because_the_product_is_offline(string raw)
        {
            var decision = Evaluate(raw);

            Assert.False(decision.IsAllowed);
            Assert.Contains(decision.Code, new[] { AssetPathCodes.RemoteUrl, AssetPathCodes.NetworkPath });
        }

        [Theory]
        [InlineData("notes.txt")]
        [InlineData("archive.zip")]
        [InlineData("logo")]
        [InlineData("logo.png.exe")]
        [InlineData("script.svg")]
        public void Anything_that_is_not_a_supported_image_type_is_refused(string raw)
        {
            var decision = Evaluate(raw);

            Assert.False(decision.IsAllowed);
            Assert.Equal(AssetPathCodes.UnsupportedType, decision.Code);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void An_empty_reference_is_refused_rather_than_throwing(string raw)
        {
            var decision = Evaluate(raw);

            Assert.False(decision.IsAllowed);
            Assert.Equal(AssetPathCodes.InvalidPath, decision.Code);
        }

        [Theory]
        [InlineData("a\0b.png")]
        [InlineData("<>|.png")]
        public void An_unusable_path_is_refused_rather_than_throwing(string raw)
        {
            var decision = Evaluate(raw);

            Assert.False(decision.IsAllowed);
            Assert.NotNull(decision.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("relative/base")]
        public void Without_an_absolute_base_directory_nothing_resolves(string baseDirectory)
        {
            // Resolving against the process's current directory would make the outcome depend on
            // where OneNote happens to be running from.
            var decision = _policy.Evaluate("logo.png", baseDirectory, AssetPolicy.Default);

            Assert.False(decision.IsAllowed);
            Assert.Equal(AssetPathCodes.InvalidPath, decision.Code);
        }

        [Fact]
        public void Every_refusal_carries_a_message_naming_the_reference()
        {
            var decision = Evaluate(@"\\server\share\logo.png");

            Assert.Contains(@"\\server\share\logo.png", decision.Message);
        }

        [Fact]
        public void The_policy_is_deterministic()
        {
            var first = Evaluate("images/logo.png");
            var second = Evaluate("images/logo.png");

            Assert.Equal(first.FullPath, second.FullPath);
            Assert.Equal(first.Code, second.Code);
        }

        [Fact]
        public void Casing_of_the_extension_does_not_matter()
        {
            Assert.True(Evaluate("LOGO.PNG").IsAllowed);
        }

        [Fact]
        public void The_policy_rejects_a_missing_asset_policy_rather_than_defaulting()
        {
            Assert.Throws<ArgumentNullException>(() => _policy.Evaluate("logo.png", Base, null));
        }
    }
}
