using NUnit.Framework;
using EOSNative.Lobbies;
using System.Collections.Generic;

namespace EOSNative.Tests
{
    public class LobbyOptionsTests
    {
        #region Defaults

        [Test]
        public void DefaultValues()
        {
            var options = new LobbyOptions();
            Assert.IsNull(options.LobbyName);
            Assert.IsNull(options.GameMode);
            Assert.IsNull(options.Map);
            Assert.IsNull(options.Region);
            Assert.IsNull(options.BucketId);
            Assert.IsNull(options.MaxPlayers);
            Assert.IsNull(options.Password);
            Assert.IsNull(options.SkillLevel);
            Assert.IsNull(options.Attributes);
            Assert.IsTrue(options.IsPublic);
            Assert.IsTrue(options.EnableVoice);
            Assert.IsFalse(options.StartMuted);
            Assert.IsTrue(options.AllowHostMigration);
            Assert.IsTrue(options.AllowCrossplay);
            Assert.AreEqual(10u, options.MaxResults);
            Assert.IsTrue(options.OnlyAvailable);
            Assert.IsFalse(options.ExcludePasswordProtected);
        }

        #endregion

        #region Fluent Builder - Shared

        [Test]
        public void FluentChaining_Shared()
        {
            var options = new LobbyOptions()
                .WithName("Test Room")
                .WithGameMode("deathmatch")
                .WithMap("arena")
                .WithRegion("us-east")
                .WithBucketId("v2")
                .WithMaxPlayers(16)
                .WithPassword("secret")
                .WithSkillLevel(1500);

            Assert.AreEqual("Test Room", options.LobbyName);
            Assert.AreEqual("deathmatch", options.GameMode);
            Assert.AreEqual("arena", options.Map);
            Assert.AreEqual("us-east", options.Region);
            Assert.AreEqual("v2", options.BucketId);
            Assert.AreEqual(16u, options.MaxPlayers);
            Assert.AreEqual("secret", options.Password);
            Assert.AreEqual(1500, options.SkillLevel);
        }

        [Test]
        public void FluentChaining_ReturnsSameInstance()
        {
            var options = new LobbyOptions();
            var returned = options.WithName("Test");
            Assert.AreSame(options, returned);
        }

        [Test]
        public void WithAttribute_CreatesDict()
        {
            var options = new LobbyOptions()
                .WithAttribute("key1", "val1")
                .WithAttribute("key2", "val2");

            Assert.IsNotNull(options.Attributes);
            Assert.AreEqual(2, options.Attributes.Count);
            Assert.AreEqual("val1", options.Attributes["key1"]);
            Assert.AreEqual("val2", options.Attributes["key2"]);
        }

        [Test]
        public void WithAttribute_OverwritesSameKey()
        {
            var options = new LobbyOptions()
                .WithAttribute("key", "old")
                .WithAttribute("key", "new");

            Assert.AreEqual(1, options.Attributes.Count);
            Assert.AreEqual("new", options.Attributes["key"]);
        }

        #endregion

        #region Fluent Builder - Create-Only

        [Test]
        public void FluentChaining_CreateOnly()
        {
            var options = new LobbyOptions()
                .WithJoinCode("1234")
                .WithVoice(false)
                .WithMutedMic()
                .WithHostMigration(false)
                .WithCrossplay(false)
                .AsPrivate();

            Assert.AreEqual("1234", options.JoinCode);
            Assert.IsFalse(options.EnableVoice);
            Assert.IsTrue(options.StartMuted);
            Assert.IsFalse(options.AllowHostMigration);
            Assert.IsFalse(options.AllowCrossplay);
            Assert.IsFalse(options.IsPublic);
        }

        [Test]
        public void AsPublic_SetsTrue()
        {
            var options = new LobbyOptions().AsPrivate().AsPublic();
            Assert.IsTrue(options.IsPublic);
        }

        [Test]
        public void WithEosLobbyId_SetsFlag()
        {
            var options = new LobbyOptions().WithEosLobbyId();
            Assert.IsTrue(options.UseEosLobbyId);
        }

        #endregion

        #region Fluent Builder - Search-Only

        [Test]
        public void FluentChaining_SearchOnly()
        {
            var options = new LobbyOptions()
                .WithMaxResults(50)
                .WithMinPlayers(2)
                .WithSkillRange(1000, 2000)
                .ExcludePassworded()
                .ExcludeGamesInProgress()
                .DesktopOnly()
                .KeyboardMouseOnly();

            Assert.AreEqual(50u, options.MaxResults);
            Assert.AreEqual(2, options.MinPlayers);
            Assert.AreEqual(1000, options.MinSkill);
            Assert.AreEqual(2000, options.MaxSkill);
            Assert.IsTrue(options.ExcludePasswordProtected);
            Assert.IsTrue(options.ExcludeInProgress);
            Assert.AreEqual("DESKTOP", options.PlatformFilter);
            Assert.AreEqual("KBM", options.InputFilter);
        }

        [Test]
        public void PlatformFilters()
        {
            Assert.AreEqual("MOBILE", new LobbyOptions().MobileOnly().PlatformFilter);
            Assert.AreEqual("SAME", new LobbyOptions().SamePlatformOnly().PlatformFilter);
        }

        [Test]
        public void InputFilters()
        {
            Assert.AreEqual("CTL", new LobbyOptions().ControllerOnly().InputFilter);
            Assert.AreEqual("SAME", new LobbyOptions().SameInputOnly().InputFilter);
            Assert.AreEqual("FAIR", new LobbyOptions().FairInputOnly().InputFilter);
        }

        #endregion

        #region ToCreateOptions

        [Test]
        public void ToCreateOptions_PreservesFields()
        {
            var options = new LobbyOptions()
                .WithName("My Room")
                .WithGameMode("coop")
                .WithMap("forest")
                .WithRegion("eu-west")
                .WithBucketId("v3")
                .WithMaxPlayers(8)
                .WithPassword("pass")
                .WithSkillLevel(1200)
                .WithJoinCode("5678")
                .WithVoice(false)
                .WithMutedMic(true)
                .WithHostMigration(false)
                .WithCrossplay(false)
                .AsPrivate();

            var create = options.ToCreateOptions();

            Assert.AreEqual("My Room", create.LobbyName);
            Assert.AreEqual("coop", create.GameMode);
            Assert.AreEqual("forest", create.Map);
            Assert.AreEqual("eu-west", create.Region);
            Assert.AreEqual("v3", create.BucketId);
            Assert.AreEqual(8u, create.MaxPlayers);
            Assert.AreEqual("pass", create.Password);
            Assert.AreEqual(1200, create.SkillLevel);
            Assert.AreEqual("5678", create.JoinCode);
            Assert.IsFalse(create.EnableVoice);
            Assert.IsTrue(create.StartMuted);
            Assert.IsFalse(create.AllowHostMigration);
            Assert.IsFalse(create.AllowCrossplay);
            Assert.IsFalse(create.IsPublic);
        }

        [Test]
        public void ToCreateOptions_DefaultBucket()
        {
            var create = new LobbyOptions().ToCreateOptions();
            Assert.AreEqual("v1", create.BucketId);
        }

        [Test]
        public void ToCreateOptions_DefaultMaxPlayers()
        {
            var create = new LobbyOptions().ToCreateOptions();
            Assert.AreEqual(4u, create.MaxPlayers);
        }

        [Test]
        public void ToCreateOptions_CopiesAttributes()
        {
            var options = new LobbyOptions()
                .WithAttribute("mode", "ranked")
                .WithAttribute("season", "3");

            var create = options.ToCreateOptions();

            Assert.IsNotNull(create.Attributes);
            Assert.AreEqual(2, create.Attributes.Count);
            Assert.AreEqual("ranked", create.Attributes["mode"]);

            // Verify it's a copy (not reference)
            options.Attributes["mode"] = "changed";
            Assert.AreEqual("ranked", create.Attributes["mode"]);
        }

        [Test]
        public void ToCreateOptions_NullAttributes()
        {
            var create = new LobbyOptions().ToCreateOptions();
            Assert.IsNull(create.Attributes);
        }

        #endregion

        #region ToSearchOptions

        [Test]
        public void ToSearchOptions_PreservesFields()
        {
            var options = new LobbyOptions()
                .WithMaxResults(25)
                .WithGameMode("ranked")
                .WithMap("arena")
                .WithRegion("us-east")
                .WithName("Pros Only")
                .WithMinPlayers(4)
                .WithMaxPlayers(16)
                .WithSkillRange(1000, 2000)
                .ExcludePassworded()
                .ExcludeGamesInProgress();

            var search = options.ToSearchOptions();

            Assert.AreEqual(25u, search.MaxResults);
            Assert.IsTrue(search.ExcludePasswordProtected);
            Assert.IsTrue(search.ExcludeInProgress);
            Assert.IsTrue(search.OnlyAvailable);
        }

        [Test]
        public void ToSearchOptions_DefaultMaxResults()
        {
            var search = new LobbyOptions().ToSearchOptions();
            Assert.AreEqual(10u, search.MaxResults);
        }

        #endregion

        #region Implicit Conversions

        [Test]
        public void ImplicitConversion_ToCreateOptions()
        {
            LobbyOptions options = new LobbyOptions().WithName("Test").WithMaxPlayers(6);
            LobbyCreateOptions create = options;

            Assert.IsNotNull(create);
            Assert.AreEqual("Test", create.LobbyName);
            Assert.AreEqual(6u, create.MaxPlayers);
        }

        [Test]
        public void ImplicitConversion_ToSearchOptions()
        {
            LobbyOptions options = new LobbyOptions().WithMaxResults(20);
            LobbySearchOptions search = options;

            Assert.IsNotNull(search);
            Assert.AreEqual(20u, search.MaxResults);
        }

        [Test]
        public void ImplicitConversion_NullSafe()
        {
            LobbyOptions options = null;
            LobbyCreateOptions create = options;
            LobbySearchOptions search = options;

            Assert.IsNull(create);
            Assert.IsNull(search);
        }

        #endregion

        #region Factory Methods

        [Test]
        public void QuickMatch_Defaults()
        {
            var options = LobbyOptions.QuickMatch();

            Assert.AreEqual(50u, options.MaxResults);
            Assert.IsTrue(options.OnlyAvailable);
            Assert.IsTrue(options.ExcludePasswordProtected);
            Assert.IsTrue(options.ExcludeInProgress);
        }

        [Test]
        public void ForGameMode_SetsGameMode()
        {
            var options = LobbyOptions.ForGameMode("deathmatch");
            Assert.AreEqual("deathmatch", options.GameMode);
        }

        [Test]
        public void ForSkillRange_SetsRange()
        {
            var options = LobbyOptions.ForSkillRange(1500, 200);

            Assert.AreEqual(1300, options.MinSkill);
            Assert.AreEqual(1700, options.MaxSkill);
            Assert.IsTrue(options.ExcludePasswordProtected);
            Assert.IsTrue(options.ExcludeInProgress);
        }

        [Test]
        public void ForSkillRange_DefaultRange()
        {
            var options = LobbyOptions.ForSkillRange(1000);

            Assert.AreEqual(800, options.MinSkill);
            Assert.AreEqual(1200, options.MaxSkill);
        }

        #endregion
    }
}
