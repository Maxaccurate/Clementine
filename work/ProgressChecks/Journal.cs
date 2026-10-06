using System;
using System.IO;
namespace ZestDrop;
internal static class Journal{public static readonly string DirectoryPath=Path.Combine(AppContext.BaseDirectory,"logs");public static void Write(string kind,object? data=null){} }
