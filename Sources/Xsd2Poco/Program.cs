using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Xsd2Poco;

class Program
{
    static async Task Main(string[] args)
    {
        var opt1 = new Option<FileInfo>(new[] {"-f", "--file"}, "file").ExistingOnly();
        var opt2 = new Option<DirectoryInfo>(new[] {"-d", "--directory"}, "directory").ExistingOnly();
        var opt3 = new Option<string>(new[] {"-n", "--namespace"}, "root namespace");
        var rootCommand = new RootCommand("Xsd2Poco")
        {
            opt1,opt2, opt3
        };

        rootCommand.SetHandler<FileInfo,DirectoryInfo, string>(BuildClasses, opt1, opt2, opt3);

        await rootCommand.InvokeAsync(args);
    }

    public static void  BuildClasses(FileInfo file, DirectoryInfo directory, string @namespace)
    {
        if (file != null && directory != null)
            throw new Exception("Please provide only one of Parameters directory or file");

        if (file == null && directory == null)
            throw new Exception("Please provide one of Parameters directory or file");

        if (file != null)
        {
            BuildClass(file, @namespace).ConfigureAwait(false).GetAwaiter().GetResult();
            return;
        }
        
        var files = directory.GetFiles("*.xsd");
        foreach (var currentfile in files)
        {
            BuildClass(currentfile, @namespace).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
    
    
    public static async Task BuildClass(FileInfo fileInfo, string namespaceString)
    {
        Console.WriteLine($"Converting {fileInfo.Name}..");
        XmlReader reader = XmlReader.Create(fileInfo.OpenRead(), new XmlReaderSettings(){Async = true});
        

        List<Element> roots = new();
        Element currentNode = null;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                var depth = reader.Depth;

                while (currentNode != null && reader.Depth <= currentNode.Depth)
                    currentNode = currentNode.ParentElement;

                if (reader.LocalName == "element")
                {
                    string name = null;
                    string type = null;
                    string reference = null;
                    int? maxOccurs = null;
                    while (reader.MoveToNextAttribute())
                    {
                        if (reader.Name == "name")
                            name = reader.Value;

                        if (reader.Name == "type")
                            type = reader.Value;
                        
                        if(reader.Name == "maxOccurs" && !string.IsNullOrWhiteSpace(reader.Value))
                            maxOccurs = reader.Value.Equals("unbounded", StringComparison.InvariantCultureIgnoreCase) ? int.MaxValue: int.Parse(reader.Value);
                        
                        if (reader.Name == "ref")
                            reference = reader.Value;
                    }

                    currentNode = MakeElement(currentNode, name, depth, type,  isList: maxOccurs > 1, reference:reference);
                    if (currentNode.ParentElement == null)
                        roots.Add(currentNode);
                    
                    continue;
                }

                if (reader.LocalName == $"attribute")
                {
                    string name = null;
                    string type = null;
                    while (reader.MoveToNextAttribute())
                    {
                        if (reader.Name == "name")
                            name = reader.Value;
                        
                        if (reader.Name == "type")
                            type = reader.Value;
                    }

                    currentNode = MakeElement(currentNode, name, depth, simpleType:type, isAttribute:true);
                    continue;
                }
                
                if (reader.LocalName == $"restriction")
                {
                    string type = null;
                    while (reader.MoveToNextAttribute())
                    {
                        if (reader.Name == "base")
                            type = reader.Value;
                    }

                    if (type != null)
                        currentNode.TypeName = type;
                    continue;
                }

                if (reader.LocalName == $"documentation" && await reader.ReadAsync())
                    currentNode.Documentation = reader.Value;
            }
        }
        
        WriteFile(roots, fileInfo, namespaceString);
    }

    private static void WriteFile(List<Element> roots, FileInfo file, string namespaceString)
    {
        var namespaceX = Path.GetFileNameWithoutExtension(file.Name);
        var builder = new StringBuilder();
        
        var elements = new List<Element>();
        GetFlatList(roots, elements);

        var complexElements = elements.Where(ele => ele.IsComplex);

        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Xml.Serialization;");

        if(string.IsNullOrWhiteSpace(namespaceString))
            builder.AppendLine($"namespace {namespaceX};");
        else
            builder.AppendLine($"namespace {namespaceString}.{namespaceX};");
        

        foreach (var element in complexElements.Where(element => !element.IsReference))
        {
            builder.AppendLine();
            if (!string.IsNullOrWhiteSpace(element.Documentation))
                builder.AppendLine($"/// <summary>{element.Documentation}</summary>");
            
            builder.AppendLine($"public partial class {element.GetTypeName()} {{");
            foreach (var subElement in element.SubElements)
            {
                var currentSubElement = subElement;
                if (currentSubElement.IsReference)
                    currentSubElement = elements.Single(element => element.Name == currentSubElement.Name && element.IsReference == false);
                    
                if(subElement.IsAttribute)
                    continue;
                builder.AppendLine();
                
                if (!string.IsNullOrWhiteSpace(currentSubElement.Documentation))
                {
                    if (!string.IsNullOrWhiteSpace(currentSubElement.Documentation))
                        builder.AppendLine($"\t/// <summary>{currentSubElement.Documentation}</summary>");
                }

                if (currentSubElement.IsList || currentSubElement.NameManipulated)
                    builder.AppendLine($"\t[XmlElement(\"{currentSubElement.OriginalName}\")]");
                builder.AppendLine(
                    $"\tpublic {currentSubElement.GetPropertyTypeName(subElement.IsReference? subElement.IsList: currentSubElement.IsList)} {currentSubElement.GetPropertyName()} {{get;set;}}{(currentSubElement.IsComplex ? " = new();" : "")}");
            }

            builder.AppendLine("}");

        }
        
        File.WriteAllText(Path.Combine(file.DirectoryName, namespaceX) + ".cs", builder.ToString());
        
    }

    private static void GetFlatList(List<Element> elements, List<Element> flatlist)
    {
        flatlist.AddRange(elements);
        foreach (var element in elements)
        {
            GetFlatList(element.SubElements, flatlist);
        }
    }

    private static Element MakeElement(Element currentNode, string name, int depth, string simpleType = null, bool isAttribute = false, bool isList = false, string reference = null)
    {
        var tmpNode = currentNode;
        currentNode = new Element();
        if (reference != null)
        {
            name = reference;
            currentNode.IsReference = true;
        }
        currentNode.Name = name;
        currentNode.TypeName = simpleType;
        currentNode.ParentElement = tmpNode;
        currentNode.IsAttribute = isAttribute;
        currentNode.IsList = isList;
        currentNode.Depth = depth;
        tmpNode?.SubElements.Add(currentNode);
        return currentNode;
    }

}

