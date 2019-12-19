using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PineappleAnalyzer.CodeAnalysis.Analyzer;
using TestHelper;

namespace PineappleAnalyzer.CodeAnalysis.CodeFix
{
    [TestClass]
    public class RequiredDefaultSwitchLabelCodeFixProviderTest : CodeFixVerifier
    {
        [TestMethod]
        public void AddDefaultCase()
        {
            var oldSource = @"
namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args) {
            switch(args.Length) {
                case 0:
                    break;
            }
        }
    }
}
            ";

            var newSource = @"
namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args) {
            switch(args.Length) {
                case 0:
                    break;
                default:
                    break;
            }
        }
    }
}
            ";

            VerifyCSharpFix(oldSource, newSource);
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
            => new RequiredDefaultSwitchLabelAnalyzer();

        protected override CodeFixProvider GetCSharpCodeFixProvider()
            => new RequiredDefaultSwitchLabelCodeFixProvider();
    }
}
