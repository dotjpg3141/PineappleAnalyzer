using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PineappleAnalyzer.CodeAnalysis.Analyzer;
using Test.Utilities;
using TestHelper;

namespace PineappleAnalyzer.CodeAnalysis.CodeFix
{
    [TestClass]
    public class RemoveUnnecessaryConditionsFromPredicateCodeFixProviderTest : CodeFixVerifier
    {
        private const string Prelude = RemoveUnnecessaryConditionsFromPredicateAnalyzerTest.Prelude;

        private const string IdNameSource = Prelude + @"
            public class TestEntity
            {
                [Key] public int Id { get; set; }
                public string Name1 { get; set; }
                public string Name2 { get; set; }
            }
        ";

        [TestMethod]
        public void RemoveSingleExpression()
        {
            var oldSource = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id == 2 && e.Name1 == ""Foo"");
                    }
                }
            ";

            var newSource = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id == 2);
                    }
                }
            ";

            VerifyCSharpFix(IdNameSource + oldSource, IdNameSource + newSource);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void RemoveTwoExpressions(int primaryKeyIndex)
        {
            var operands = new List<string>()
            {
                @"e.Name1 == ""Foo""",
                @"e.Name2 == ""Bar""",
            };
            operands.Insert(primaryKeyIndex, "e.Id == 2");

            var oldSource = $@"
                class Program
                {{
                    static void Main() {{
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => {string.Join(" && ", operands)});
                    }}
                }}
            ";

            var newSource = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id == 2);
                    }
                }
            ";

            VerifyCSharpFix(IdNameSource + oldSource, IdNameSource + newSource);
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
            => new RemoveUnnecessaryConditionsFromPredicateAnalyzer();

        protected override CodeFixProvider GetCSharpCodeFixProvider()
            => new RemoveUnnecessaryConditionsFromPredicateCodeFixProvider();

        protected override ReferenceAssemblies GetReferenceAssemblies()
             => AdditionalMetadataReferences.DefaultWithEntityFramework6;
    }
}
