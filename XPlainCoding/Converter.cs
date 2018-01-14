using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CSharp;

namespace XPlainCoding
{
    class Converter
    {
        //CodeTypeDeclaration globalClass = new CodeTypeDeclaration("Global");


        private string MAKE = $@"\b(?:\k)(?:\k)? {TYPE}(?:\k| {VARIABLE}\b)?";//?((?: \k)? {VARIABLE})?";//(?: \k ({VALUE}))?";
        private string NAME = $@"\b(?:\k)(?:\k)? {VARIABLE}\b";
        private string HAS = $@"\b(?:{TYPE}|\k) (?:\k)(?: {TYPE}\k| {TYPE} {VARIABLE}| {TYPE})?\b";
        public string[] makeKeywords = {"make","create","instanciate"};
        public string[] preKeywords = { @"\w* new", "this", "a", "an", "the"};
        public string[] nameKeywords = { "name is", @"call\w*", @"name\w*", "with the name"};
        public string[] itKeywords = { "it", @"the \w+ " };
        public string[] hasKeywords = { "has the variable", "has a variable", "has an", "has a", "has", "with the variable", "with a variable", "with an" , "with a" };
        public string[] initValueKeywords = { "starting .*", "=" };
        public string[] fillerKeywords = { "and" };
        const string TYPE = @"""?([A-Za-z]\w*)""?";
        const string VARIABLE = @"""?([A-Za-z]\w*)""?";
        const string VALUE = @"""?(-?[\w]+)""?";

        private static CodeCompileUnit currentUnit;
        private static CodeNamespace currentNamespace;
        private static CodeTypeDeclaration currentType;
        private static CodeMemberMethod currentMethod;
        private static CodeMemberField currentField;
        private static CodeVariableDeclarationStatement currentStatement;
        private static CodeVariableReferenceExpression currentVariable;


        public Converter()
        {
            currentUnit = new CodeCompileUnit();
            currentNamespace = new CodeNamespace("Standard");
            currentNamespace.Imports.Add(new CodeNamespaceImport("System"));
            currentUnit.Namespaces.Add(currentNamespace);
            CodeTypeDeclaration currentType = new CodeTypeDeclaration("Program");
            currentNamespace.Types.Add(currentType);
            currentType.IsClass = true;
            //currentType.TypeAttributes = TypeAttributes.Public;
            currentMethod = new CodeMemberMethod()
            {
                Attributes = MemberAttributes.Static,
                Name = "Main",
                Parameters = { new CodeParameterDeclarationExpression(typeof(string[]), "args") }
            };
            currentType.Members.Add(currentMethod);
        }

        public string ConvertToCode(string input)
        {
            var results = new List<Result>();
            results.AddRange(MatchMAKE(input));
            results.AddRange(MatchNAME(input));
            results.AddRange(MatchHAS(input));
            results = results.OrderBy(x => x.index).ToList();
            foreach (var result in results)
            {
                result.Implement();
            }
            StringBuilder sb = new StringBuilder();
            CSharpCodeProvider provider = new CSharpCodeProvider();
            TextWriter writer = new StringWriter(sb);

            provider.GenerateCodeFromCompileUnit(currentUnit, writer, new CodeGeneratorOptions(){});
            int ns = 0;
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n')
                {
                    ns++;
                }
                if (ns == 10)
                {
                    sb.Remove(0, i);
                    break;
                }
            }
            return sb.ToString();
        }

        abstract class Result
        {
            public int index;

            protected Result(int index)
            {
                this.index = index;
            }

            public abstract void Implement();
        }

        class MAKEResult : Result
        {
            public string type;
            public string name;

            public MAKEResult(string type, string name, int index) : base(index)
            {
                this.type = type;
                this.name = name;
            }

            public override void Implement()
            {
                string varName = name;
                if (varName == "") varName = type + "0";
                Type typeRef = GetPrimitiveType(type);

                if (typeRef == null)
                {
                    var existingType = currentNamespace.Types.Cast<CodeTypeDeclaration>().FirstOrDefault(x => x.Name == type);
                    if (existingType == null)
                    {
                        currentType = new CodeTypeDeclaration(type);
                        currentNamespace.Types.Add(currentType);
                    }
                    currentStatement = new CodeVariableDeclarationStatement(type, varName, new CodeObjectCreateExpression(type));
                }
                else
                {
                    currentStatement = new CodeVariableDeclarationStatement(typeRef, varName);
                }

                currentMethod.Statements.Add(currentStatement);

                currentField = null;
            }
        }

        List<MAKEResult> MatchMAKE(string input)
        {
            List<MAKEResult> list = new List<MAKEResult>();
            string regex = MAKE;
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOrRegex(makeKeywords));
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOptionalOrRegex(preKeywords));
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOptionalOrRegex(fillerKeywords.Plus(nameKeywords).Plus(itKeywords).Plus(hasKeywords)));
            var matches = Regex.Matches(input, regex);
            foreach (Match match in matches)
            {
                list.Add(new MAKEResult(match.Groups[1].Value, match.Groups[2].Value, match.Index));
            }

            return list;
        }

        class NAMEResult : Result
        {
            string name;

            public NAMEResult(string name, int index) : base(index)
            {
                this.name = name;
            }

            public override void Implement()
            {
                if (currentField != null)
                {
                    currentField.Name = name;
                }
                else if (currentStatement != null)
                {
                    currentStatement.Name = name;
                }
                else
                {
                    
                }
            }
        }

        List<NAMEResult> MatchNAME(string input)
        {
            List<NAMEResult> list = new List<NAMEResult>();
            string regex = NAME;
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOrRegex(nameKeywords));
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOptionalOrRegex(itKeywords));
            var matches = Regex.Matches(input, regex);
            foreach (Match match in matches)
            {
                list.Add(new NAMEResult(match.Groups[1].Value, match.Index));
            }

            return list;
        }

        class HASResult : Result
        {
            public string type;
            public string varType;
            public string varName;

            public HASResult(string type, string varType, string varName, int index) : base(index)
            {
                this.type = type;
                this.varType = varType;
                this.varName = varName;
            }

            public override void Implement()
            {
                CodeTypeDeclaration typeRef = null;
                if (type != "") typeRef = TryGetType(type);
                if (typeRef == null) typeRef = currentType;
                if (varType == "")
                {
                    currentField = new CodeMemberField(typeof(object), varName);
                    typeRef.Members.Add(currentField);
                }
                else if (varName == "")
                {
                    if (TryGetType(varType) != null)
                    {
                        currentField = new CodeMemberField(varType, varType + "0");
                        typeRef.Members.Add(currentField);
                    }
                    else
                    {
                        var primitiveType = GetPrimitiveType(varType);
                        if (primitiveType != null)
                        {
                            currentField = new CodeMemberField(primitiveType, varType + "0");
                            typeRef.Members.Add(currentField);
                        }
                        else
                        {
                            currentField = new CodeMemberField(typeof(object), varType);
                            typeRef.Members.Add(currentField);
                        }
                    }
                }
                else
                {
                    var primitiveType = GetPrimitiveType(varType);
                    if (primitiveType != null)
                    {
                        currentField = new CodeMemberField(primitiveType, varName);
                        typeRef.Members.Add(currentField);
                    }
                    else
                    {
                        currentField = new CodeMemberField(varType, varName);
                        typeRef.Members.Add(currentField);
                    }
                }
            }
        }

        List<HASResult> MatchHAS(string input)
        {
            List<HASResult> list = new List<HASResult>();
            string regex = HAS;
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOrRegex(itKeywords.Plus(fillerKeywords)));
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOrRegex(hasKeywords));
            regex = regex.ReplaceFirstOccurrence(@"\k", ArrayToOptionalOrRegex(fillerKeywords.Plus(nameKeywords)));
            var matches = Regex.Matches(input, regex);
            foreach (Match match in matches)
            {
                string type = match.Groups[1].Value;
                string varType = "";
                string varName = "";
                if (match.Groups[2].Value != "") varType = match.Groups[2].Value;
                if (match.Groups[5].Value != "") varType = match.Groups[5].Value;

                if (varType == "")
                {
                    varType = match.Groups[3].Value;
                    varName = match.Groups[4].Value;
                }

                list.Add(new HASResult(type, varType, varName, match.Index));
            }

            return list;
        }



        //public static string Convert(string paragraphs)
        //{
        //    //AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("Global"), AssemblyBuilderAccess.Run);
        //    //ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
        //    //TypeBuilder typeBuilder = moduleBuilder.DefineType("Global");
        //    CodeCompileUnit unit = new CodeCompileUnit();
        //    CodeNamespace nameSpace = new CodeNamespace("Standard");
        //    unit.Namespaces.Add(nameSpace);
        //    CodeTypeDeclaration programClass = new CodeTypeDeclaration("Program");
        //    nameSpace.Types.Add(programClass);
        //    programClass.IsClass = true;
        //    programClass.TypeAttributes = TypeAttributes.Public;
        //    programClass.Members.Add(new CodeMemberMethod()
        //    {
        //        Attributes = MemberAttributes.Static,
        //        Name = "Main",
        //        Parameters = {new CodeParameterDeclarationExpression(typeof(string[]),"args")}
        //    });



        //    ThereIs(paragraphs, nameSpace);
        //    Has(paragraphs, nameSpace);
        //    Can(paragraphs, nameSpace);
        //    Define(paragraphs, nameSpace);
        //    //SetTo(paragraphs, programClass);




        //    StringBuilder sb = new StringBuilder();
        //    CSharpCodeProvider provider = new CSharpCodeProvider();
        //    TextWriter writer = new StringWriter(sb);

        //    provider.GenerateCodeFromCompileUnit(unit, writer, new CodeGeneratorOptions());
        //    int ns = 0;
        //    for (int i = 0; i < sb.Length; i++)
        //    {
        //        if (sb[i] == '\n')
        //        {
        //            ns++;
        //        }
        //        if (ns == 10)
        //        {
        //            sb.Remove(0, i);
        //            break;
        //        }
        //    }
        //    return sb.ToString();
        //}

        //private static void ThereIs(string paragraphs, CodeNamespace nameSpace)
        //{
        //    var matches = Regex.Matches(paragraphs, $@"\bthere is(?: a| the)? ({VARIABLE})", RegexOptions.IgnoreCase);
        //    foreach (Match match in matches)
        //    {
        //        var typeName = match.Groups[1].Value;
        //        CodeTypeDeclaration type = new CodeTypeDeclaration(typeName);
        //        nameSpace.Types.Add(type);
        //    }
        //}

        //private static void Has(string paragraphs, CodeNamespace nameSpace)
        //{
        //    var matches = Regex.Matches(paragraphs, $@"\b({VARIABLE}) has(?: a|) ({VARIABLE})(?:\s?=\s?({VALUE}))?", RegexOptions.IgnoreCase);
        //    foreach (Match match in matches)
        //    {
        //        var typeName = match.Groups[1].Value;
        //        var varName = match.Groups[2].Value;
        //        var type = typeof(object);
        //        CodeSnippetExpression initVal = null;

        //        if (match.Groups[3].Value != "")
        //        {
        //            var typevalue = GetTypeByValue(match.Groups[3].Value);
        //            type = typevalue.Item1;
        //            initVal = new CodeSnippetExpression(typevalue.Item2);
        //        }


        //        CodeMemberField field = new CodeMemberField(type, varName)
        //        {
        //            Attributes = MemberAttributes.Public,
        //            InitExpression = initVal
        //        };
        //        nameSpace.Types.Cast<CodeTypeDeclaration>().FirstOrDefault(x => x.Name == typeName)?.Members.Add(field);
        //    }
        //}

        //private static void Can(string paragraphs, CodeNamespace nameSpace)
        //{
        //    var matches = Regex.Matches(paragraphs, $@"\b({VARIABLE}) can ({VARIABLE})",RegexOptions.IgnoreCase);
        //    foreach (Match match in matches)
        //    {
        //        var typeName = match.Groups[1].Value;
        //        var methodName = match.Groups[2].Value;

        //        CodeMemberMethod method = new CodeMemberMethod()
        //        {
        //            Name = methodName,
        //            Attributes = MemberAttributes.Public | MemberAttributes.Final
        //        };
        //        nameSpace.Types.Cast<CodeTypeDeclaration>().FirstOrDefault(x => x.Name == typeName)?.Members.Add(method);
        //    }
        //}

        //private static void Define(string paragraphs, CodeNamespace nameSpace)
        //{
        //    var matches = Regex.Matches(paragraphs, $@"\b({VARIABLE})( sets| increases| decreases)(?: the)? ({VARIABLE})(?: to| by) ({VALUE})", RegexOptions.IgnoreCase);
        //    foreach (Match match in matches)
        //    {
        //        var methodName = match.Groups[1].Value;
        //        var operation = match.Groups[2].Value;
        //        var variableName = match.Groups[3].Value;
        //        var value = match.Groups[4].Value;

        //        switch (operation)
        //        {
        //            case " sets":
        //                operation = "=";
        //                break;
        //            case " increases":
        //                operation = "+=";
        //                break;
        //            case " decreases":
        //                operation = "-=";
        //                break;
        //        }
        //        foreach (CodeTypeDeclaration type in nameSpace.Types)
        //        {
        //            var method = type.Members.Cast<CodeTypeMember>().FirstOrDefault(x => x.Name == methodName);
        //            if (method != null)
        //            {
        //                ((CodeMemberMethod)method).Statements.Add( new CodeSnippetExpression($"{variableName} {operation} {value}"));
        //            }
        //        }
        //    }
        //}

        ////private static void SetTo(string paragraphs, CodeTypeDeclaration classs)
        //        //{
        //        //    var matches = Regex.Matches(paragraphs, $@" ({VARIABLE}) to (\d+[.]\d+|\w+)", RegexOptions.IgnoreCase);
        //        //    foreach (Match match in matches)
        //        //    {
        //        //        Type type;
        //        //        string name = match.Groups[1].Value;
        //        //        string value = match.Groups[2].Value;

        //        //        int outi;
        //        //        float outf;
        //        //        if (int.TryParse(value, out outi))
        //        //        {
        //        //            type = typeof(int);
        //        //        }
        //        //        else if (float.TryParse(value, out outf))
        //        //        {
        //        //            type = typeof(float);
        //        //        }
        //        //        else
        //        //        {
        //        //            type = typeof(string);
        //        //            value = $@"""{value}""";
        //        //        }
        //        //        CodeMemberField member = classs.Members.Cast<CodeTypeMember>().FirstOrDefault(x => x.Name == name) as CodeMemberField;
        //        //        if (member != null)
        //        //        {
        //        //            member.InitExpression = new CodeSnippetExpression(value);
        //        //        }
        //        //        else
        //        //        {
        //        //            member = new CodeMemberField(type, name);
        //        //            member.InitExpression = new CodeSnippetExpression(value);
        //        //            classs.Members.Add(member);
        //        //        }
        //        //    }
        //        //}



        private static Tuple<Type,string> GetTypeByValue(string input)
        {
            Type type;
            string value = input;
            if (input.Length >= 2 && input[0] == '"' && input[input.Length - 1] == '"')
            {
                type = typeof(string);
            }
            else
            {
                int outi;
                float outf;
                if (int.TryParse(value, out outi))
                {
                    type = typeof(int);
                }
                else if (float.TryParse(value, out outf))
                {
                    type = typeof(float);
                }
                else
                {
                    type = typeof(string);
                    value = $@"""{value}""";
                }
            }

            return new Tuple<Type, string>(type, value);
        }

        Dictionary<string,string> AlternativeTypeNames = new Dictionary<string, string>()
        {
            {"number", "int"},
            {"text", "string"}
        };

        private string ArrayToOrRegex(string[] keywords)
        {
            string regex = "";
            for (int i = 0; i < keywords.Length - 1; i++)
            {
                regex += keywords[i] + "|";
            }
            regex += keywords[keywords.Length - 1];
            return regex;
        }

        private string ArrayToOptionalOrRegex(string[] keywords)
        {
            string regex = " ";
            for (int i = 0; i < keywords.Length-1; i++)
            {
                regex += keywords[i] + "| ";
            }
            regex += keywords[keywords.Length - 1];
            return regex;
        }

        private CodeMemberField TryGetField(string name)
        {
            foreach (CodeTypeMember member in currentType.Members)
            {
                if (member.Name == name)
                {
                    var field = member as CodeMemberField;
                    return field;
                }
            }

            return null;
        }

        private static CodeTypeDeclaration TryGetType(string name)
        {
            foreach (CodeTypeDeclaration type in currentNamespace.Types)
            {
                if (type.Name == name)
                {
                    return type;
                }
            }

            return null;
        }

        private static Type GetPrimitiveType(string name)
        {
            switch (name)
            {
                case "int":
                    return typeof(int);
                case "string":
                    return typeof(string);
                case "float":
                    return typeof(float);
                case "double":
                    return typeof(double);
                case "char":
                    return typeof(char);
            }
            return null;
        }



    }
}