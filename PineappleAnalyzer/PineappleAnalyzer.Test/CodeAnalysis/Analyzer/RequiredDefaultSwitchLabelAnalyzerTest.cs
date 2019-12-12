using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestHelper;

namespace PineappleAnalyzer.CodeAnalysis.Analyzer
{
    [TestClass]
    public class RequiredDefaultSwitchLabelAnalyzerTest : DiagnosticVerifier
    {
        [TestMethod]
        public void Empty()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                    }
                }
            ";

            VerifyCSharpDiagnostic(source);
        }

        [TestMethod]
        public void DefaultSwitchLabel()
        {
            var source = @"
                namespace ConsoleApplication1
                {
                    class Program
                    {
                        static void Main() {
                            switch(0) {
                                default:
                                    break;
                            }
                        }
                    }
                }
            ";

            VerifyCSharpDiagnostic(source);
        }

        [TestMethod]
        public void CaseSwitchLabel()
        {
            var source = @"
                namespace ConsoleApplication1
                {
                    class Program
                    {
                        static void Main() {
                            switch(0) {
                                case 0:
                                    break;
                            }
                        }
                    }
                }
            ";

            var expected = new DiagnosticResult()
            {
                Id = DiagnosticDescriptors.RequiredDefaultSwitchLabel.Id,
                Message = DiagnosticDescriptors.RequiredDefaultSwitchLabel.MessageFormat.ToString(CultureInfo.InvariantCulture),
                Severity = DiagnosticDescriptors.RequiredDefaultSwitchLabel.DefaultSeverity,
                Locations = new[] {
                    new DiagnosticResultLocation("Test0.cs", 7, 29)
                }
            };

            VerifyCSharpDiagnostic(source, expected);
        }

        //[TestMethod]
        //public void CasePatternSwitchLabel()
        //{
        //    var source = @"
        //        namespace ConsoleApplication1
        //        {
        //            class Program
        //            {
        //                static void Main() {
        //                    switch(0) {
        //                        case var foo:
        //                            break;
        //                    }
        //                }
        //            }
        //        }
        //    ";

        //    VerifyCSharpDiagnostic(source);
        //}

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new RequiredDefaultSwitchLabelAnalyzer();
        }
    }
}
