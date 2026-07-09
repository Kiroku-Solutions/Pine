using System;
using System.Reflection;
using Avalonia.Input;

foreach(var t2 in typeof(DragDrop).GetMethods()) {
    if (t2.Name.Contains("DragDrop")) {
        Console.Write(t2.Name + " params: ");
        foreach(var p in t2.GetParameters()) Console.Write(p.ParameterType.Name + " " + p.Name + ", ");
        Console.WriteLine();
    }
}

var dtType = typeof(DragEventArgs).Assembly.GetType("Avalonia.Input.DataTransfer");
if (dtType != null) {
    Console.WriteLine("DataTransfer constructors:");
    foreach(var c in dtType.GetConstructors()) Console.WriteLine(c.ToString());
    Console.WriteLine("DataTransfer methods:");
    foreach(var m in dtType.GetMethods(BindingFlags.Public | BindingFlags.Instance)) Console.WriteLine(m.Name);
}

