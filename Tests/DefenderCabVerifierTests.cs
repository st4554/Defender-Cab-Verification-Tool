using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Defender_Cab_Verification_Tool.Tests
{
    [TestClass]
    public class DefenderCabVerifierTests
    {
        [TestMethod]
        public void GetLatestCab_NoFiles_ReturnsNull()
        {
            var type = typeof(Defender_Cab_Verification_Tool.DefenderCabVerifier);
            var method = type.GetMethod("GetLatestCab", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "GetLatestCab method not found");

            // Use an unlikely pattern so no files match
            var result = method.Invoke(null, new object[] { "defender-dism-nonexistent-*.cab" });
            Assert.IsNull(result);
        }

        [TestMethod]
        public void VerifyAll_NoCabs_DoesNotThrow()
        {
            var type = typeof(Defender_Cab_Verification_Tool.DefenderCabVerifier);
            var verifyMethod = type.GetMethod(
                "VerifyAll",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(Action<Defender_Cab_Verification_Tool.FileVerificationResult>), typeof(IProgress<int>), typeof(Action<string>), typeof(System.Threading.CancellationToken) },
                null);

            Assert.IsNotNull(verifyMethod, "VerifyAll method not found");

            bool anyFileCallback = false;
            Action<Defender_Cab_Verification_Tool.FileVerificationResult> fileCallback = r => anyFileCallback = true;
            var progress = new Progress<int>(_ => { });
            Action<string> log = _ => { };

            // Should not throw when there are no CABs present
            verifyMethod.Invoke(null, new object[] { fileCallback, progress, log, System.Threading.CancellationToken.None });

            Assert.IsFalse(anyFileCallback, "Callback should not be called when there are no CABs.");
        }
    }
}