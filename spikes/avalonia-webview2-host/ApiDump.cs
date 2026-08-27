using Avalonia.Controls;
var type = typeof(WebView2);
Console.WriteLine(type.AssemblyQualifiedName);
foreach (var member in type.GetMembers().Where(member => member.MemberType is System.Reflection.MemberTypes.Event or System.Reflection.MemberTypes.Property or System.Reflection.MemberTypes.Method).OrderBy(member => member.Name))
{
    Console.WriteLine(member);
}
