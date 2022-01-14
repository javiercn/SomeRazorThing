using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.IO;
using System.Text.Unicode;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.CodeAnalysis.CSharp;

namespace RazorVisualizer
{
    public class ApiController : Controller
    {
        [HttpPost("/[controller]/[action]")]
        public IActionResult Parse([FromBody] Source source)
        {
            var document = RazorSourceDocument.Create(source.Content, fileName: null);
            var options = RazorParserOptions.Create(builder => {
                foreach (var directive in GetDirectives())
                {
                    builder.Directives.Add(directive);
                }
            });
            var parser = new RazorParser(options);
            var tree = parser.Parse(document);
            var result = TreeSerializer.Serialize(tree);
            return Content(result);
        }

        internal class ConfigureRazorCodeGenerationOptions : RazorEngineFeatureBase, IConfigureRazorCodeGenerationOptionsFeature
        {
            private readonly Action<RazorCodeGenerationOptionsBuilder> _action;

            public ConfigureRazorCodeGenerationOptions(Action<RazorCodeGenerationOptionsBuilder> action)
            {
                _action = action;
            }

            public int Order { get; set; }

            public void Configure(RazorCodeGenerationOptionsBuilder options) => _action(options);
        }

        [HttpPost("/[controller]/[action]")]
        public IActionResult ParseIR([FromBody] Source source)
        {
            var document = RazorSourceDocument.Create(source.Content, fileName: null);
            var options = RazorParserOptions.Create(builder => {
                foreach (var directive in GetDirectives())
                {
                    builder.Directives.Add(directive);
                }
            });

            var sourceDocument = CreateSourceDocument(source.Content);

            var engine = RazorProjectEngine.Create(RazorConfiguration.Default, RazorProjectFileSystem.Empty, b =>
            {
                b.Features.Add(new DefaultTypeNameFeature());
                b.SetRootNamespace("RazorVisualizer");

                b.Features.Add(new ConfigureRazorCodeGenerationOptions(options =>
                {
                    options.SuppressMetadataSourceChecksumAttributes = true;
                    options.SupportLocalizedComponentNames = false;
                }));

                b.Features.Add(new DefaultTagHelperDescriptorProvider());

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);

                b.SetCSharpLanguageVersion(LanguageVersion.CSharp10);
            });

            var codeDocument = engine.Process(sourceDocument, FileKinds.Component, null, null);

            var irDocument = codeDocument.GetDocumentIntermediateNode();
            var output = SerializeIR(irDocument);
            //output = NormalizeNewLines(output, replaceWith: "LF");
            return Json(output);
        }

        [HttpPost("/[controller]/[action]")]
        public IActionResult NewParse([FromBody] Source source)
        {
            var document = RazorSourceDocument.Create(source.Content, fileName: null);
            var options = RazorParserOptions.Create(builder => {
                foreach (var directive in GetDirectives())
                {
                    builder.Directives.Add(directive);
                }
            });
            var context = new ParserContext(document, options);
            var codeParser = new CSharpCodeParser(GetDirectives(), context);
            var markupParser = new HtmlMarkupParser(context);

            codeParser.HtmlParser = markupParser;
            markupParser.CodeParser = codeParser;

            var root = markupParser.ParseDocument().CreateRed();
            var result = NewTreeSerializer.Serialize(root);
            return Content(result);
        }

        private static IEnumerable<DirectiveDescriptor> GetDirectives()
        {
            var directives = new DirectiveDescriptor[]
            {
                DirectiveDescriptor.CreateDirective(
                    "inject",
                    DirectiveKind.SingleLine,
                    builder =>
                    {
                        builder
                            .AddTypeToken()
                            .AddMemberToken();

                        builder.Usage = DirectiveUsage.FileScopedMultipleOccurring;
                    }),
                DirectiveDescriptor.CreateDirective(
                    "model",
                    DirectiveKind.SingleLine,
                    builder =>
                    {
                        builder.AddTypeToken();
                        builder.Usage = DirectiveUsage.FileScopedSinglyOccurring;
                    }),
                DirectiveDescriptor.CreateDirective(
                    "namespace",
                    DirectiveKind.SingleLine,
                    builder =>
                    {
                        builder.AddNamespaceToken();
                        builder.Usage = DirectiveUsage.FileScopedSinglyOccurring;
                    }),
                DirectiveDescriptor.CreateDirective(
                    "page",
                    DirectiveKind.SingleLine,
                    builder =>
                    {
                        builder.AddOptionalStringToken();
                        builder.Usage = DirectiveUsage.FileScopedSinglyOccurring;
                    }),
                DirectiveDescriptor.CreateDirective(
                    SyntaxConstants.CSharp.FunctionsKeyword,
                    DirectiveKind.CodeBlock,
                    builder =>
                    {
                    }),
                DirectiveDescriptor.CreateDirective(
                    SyntaxConstants.CSharp.InheritsKeyword,
                    DirectiveKind.SingleLine,
                    builder =>
                    {
                        builder.AddTypeToken();
                        builder.Usage = DirectiveUsage.FileScopedSinglyOccurring;
                    }),
                DirectiveDescriptor.CreateDirective(
                    SyntaxConstants.CSharp.SectionKeyword,
                    DirectiveKind.RazorBlock,
                    builder =>
                    {
                        builder.AddMemberToken();
                    }),
            };

            return directives;
        }

        public static RazorSourceDocument CreateSourceDocument(
            string content = "Hello, world!",
            Encoding encoding = null,
            bool normalizeNewLines = false,
            string filePath = "test.cshtml",
            string relativePath = "test.cshtml")
        {
            if (normalizeNewLines)
            {
                content = NormalizeNewLines(content);
            }

            var properties = new RazorSourceDocumentProperties(filePath, relativePath);
            return new StringSourceDocument(content, encoding ?? Encoding.UTF8, properties);
        }

        private static string NormalizeNewLines(string content, string replaceWith = "\r\n")
        {
            return Regex.Replace(content, "(?<!\r)\n", replaceWith, RegexOptions.None, TimeSpan.FromSeconds(10));
        }

        private static string SerializeIR(IntermediateNode node)
        {
            var formatter = new DebuggerDisplayFormatter();
            formatter.FormatTree(node);
            return formatter.ToString();
        }

        public class Source
        {
            public string Content { get; set; }
        }
    }
}