using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Xsd2Poco;

public class Element
{
    private static Regex _regex = new Regex("[^a-zA-Z0-9_]+", RegexOptions.Compiled);
    public static string RemoveSpecialCharacters(string str)
    {
        return _regex.Replace(str, "");
    }
    
    private string _name;

    public string Name
    {
        get => _name;
        set
        {
            if (value != null)
            {
                var tmp = RemoveSpecialCharacters(value);
                _name = tmp;
                OriginalName = value;
            }
        }
    }
    
    public int Depth { get; set; }
    public string OriginalName { get; set; }

    public bool NameManipulated => Name != OriginalName;

    public bool IsComplex => TypeName == null;
    
    //Only Set if SimpleType
    public string TypeName { get; set; }
    
    public bool IsAttribute { get; set; }
    
    public bool IsReference { get; set; }
    
    public bool IsList { get; set; }
    
    public string Documentation { get; set; }

    public List<Element> SubElements { get; set; } = new();
    public Element ParentElement { get; set; }

    public override string ToString()
    {
        return $"{Name}: {(IsList ? "List of ":"")}{TypeName ?? "Complex"} {(IsAttribute ? "[Attribute]": string.Empty)}";
    }

    public string GetTypeName()
    {
        if (IsComplex)
            return Name;

        return GetTypeNameInternal(TypeName);
    }
    
    public string GetPropertyTypeName(bool asList)
    {
        if (IsComplex)
            return $"{(asList? "List<":"")}{Name}{(asList?">":"")}";

        return GetTypeNameInternal(TypeName);
    }

    private string GetTypeNameInternal(string typeName)
    {
        var splitted = typeName.Split(":");
        if (splitted.Length > 1)
            typeName = splitted[1].ToLower();

        var retVal = typeName switch
        {
            "string" => "string",
            "unsignedlong" => "ulong",
            "unsignedbyte" => "byte",
            "unsignedint" => "uint",
            "unsignedshort" => "ushort",
            "short" => "short",
            "long" => "long",
            "byte" => "sbyte",
            "int" => "int",
            _ => typeName
        };

        return retVal;
    }

    public string GetPropertyName()
    {
        if (IsList)
            return Name + "s";
        return Name;
    }
}