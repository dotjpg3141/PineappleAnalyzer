using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Test.Utilities;
using TestHelper;

namespace PineappleAnalyzer.CodeAnalysis.Analyzer
{
    [TestClass]
    public class RemoveUnnecessaryConditionsFromPredicateAnalyzerTest : DiagnosticVerifier
    {
        private const string IdNameSource = @"
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using System.Data.Entity;
            using System.Linq;

            public class TestContext : DbContext
            {
                public DbSet<TestEntity> DbTestEntities { get; set; }
            }

            public class TestEntity
            {
                [Key] public int Id { get; set; }
                public string Name { get; set; }
            }
        ";

        private const string Id1Id2NameSource = @"
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using System.Data.Entity;
            using System.Linq;

            public class TestContext : DbContext
            {
                public DbSet<TestEntity> DbTestEntities { get; set; }
            }

            public class TestEntity
            {
                [Key] public int Id1 { get; set; }
                [Key] public int Id2 { get; set; }
                public string Name { get; set; }
            }
        ";

        private static readonly Regex NewLineRegex = new Regex("\r?\n", RegexOptions.Compiled);
        private static readonly int IdNameLineOffset = NewLineRegex.Split(IdNameSource).Length - 1;
        private static readonly int Id1Id2NameLineOffset = NewLineRegex.Split(Id1Id2NameSource).Length - 1;

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
        public void IdName_Empty()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                    }
                }
            ";

            VerifyCSharpDiagnostic(IdNameSource + source);
        }

        [TestMethod]
        public void IdName_Where_Id()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id == 2);
                    }
                }
            ";

            VerifyCSharpDiagnostic(IdNameSource + source);
        }

        [TestMethod]
        public void IdName_Where_IdAndName()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id == 2 && e.Name == ""Foo"");
                    }
                }
            ";

            var expected = new DiagnosticResult()
            {
                Id = DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.Id,
                Message = DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.MessageFormat.ToString(CultureInfo.InvariantCulture),
                Severity = DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.DefaultSeverity,
                Locations = new[] {
                    new DiagnosticResultLocation("Test0.cs", 6 + IdNameLineOffset, 61)
                }
            };

            VerifyCSharpDiagnostic(IdNameSource + source, expected);
        }

        [TestMethod]
        public void Id1Id2Name_Empty()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                    }
                }
            ";

            VerifyCSharpDiagnostic(Id1Id2NameSource + source);
        }

        [TestMethod]
        public void Id1Id2Name_Where_Id1()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id1 == 2);
                    }
                }
            ";

            VerifyCSharpDiagnostic(Id1Id2NameSource + source);
        }

        [TestMethod]
        public void Id1Id2Name_Where_Id1AndId2()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id1 == 2 && e.Id2 == 3);
                    }
                }
            ";

            VerifyCSharpDiagnostic(Id1Id2NameSource + source);
        }

        [TestMethod]
        public void Id1Id2Name_Where_Id1AndName()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id1 == 2 && e.Name == ""Foo"");
                    }
                }
            ";

            VerifyCSharpDiagnostic(Id1Id2NameSource + source);
        }

        [TestMethod]
        public void Id1Id2Name_Where_Id1AndId2AndName()
        {
            var source = @"
                class Program
                {
                    static void Main() {
                        var context = new TestContext();
                        var entity = context.DbTestEntities.Where(e => e.Id1 == 2 && e.Id2 == 3 && e.Name == ""Foo"");
                    }
                }
            ";

            var expected = new DiagnosticResult()
            {
                Id = DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.Id,
                Message = DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.MessageFormat.ToString(CultureInfo.InvariantCulture),
                Severity = DiagnosticDescriptors.RemoveUnnecessaryConditionsFromPredicate.DefaultSeverity,
                Locations = new[] {
                    new DiagnosticResultLocation("Test0.cs", 6 + Id1Id2NameLineOffset, 61)
                }
            };

            VerifyCSharpDiagnostic(Id1Id2NameSource + source, expected);
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
            => new RemoveUnnecessaryConditionsFromPredicateAnalyzer();

        protected override ReferenceAssemblies GetReferenceAssemblies()
            => AdditionalMetadataReferences.DefaultWithEntityFramework6;
    }
}
