
<div align="center">
	<h1>Reloaded.Hooks: Getting Started</h1>
	<img src="https://i.imgur.com/BjPn7rU.png" width="150" align="center" />
	<br/> <br/>
	<strong><i>Detours library with Blackjack and Hookers.</i></strong>
</div>

## Page Information

🕒  Reading Time: 05-10 Minutes

## Introduction

In this article, will quickly-ish demonstrate simple usage allowing you to get started with Reloaded.Hooks.

As this library, part of *Reloaded-Mod-Loader* was originally created to deal with modifying and reverse engineering games; many of the examples in this article will show game functions as opposed to common APIs. That said - nothing changes when hooking arbitrary APIs.

## Table of Contents
- [Adding Reloaded.Hooks to your project.](#adding-reloadedhooks-to-your-project)
- [Prologue: Native Functions](#prologue-native-functions)
  - [Defining Reloaded-Compatible Delegates](#defining-reloaded-compatible-delegates)
    - [Examples](#examples)
  - [Calling Functions](#calling-functions)
  - [Hooking Functions](#hooking-functions)
    - [Example](#example)
    - [Hooking Functions: Remarks](#hooking-functions-remarks)
  - [Calling Function Pointers](#calling-function-pointers)
    - [Function Pointers: Performance](#function-pointers-performance)
  - [Function Pointers to C# Functions](#function-pointers-to-c-functions)


## Adding Reloaded.Hooks to your project.
1.  Open/Create project in Visual Studio.
2.  Right-click your project within the Solution Explorer and select “Manage NuGet Packages…”.
3.  Search for "Reloaded.Hooks”.
4.  Install the package.

## Prologue: Native Functions

Calling, hooking and performing other operations with native functions with Reloaded.Hooks is performed through the use of delegate declarations.

Reloaded.Hooks' main library is able to create individual *delegate* instances given supplied function addresses in memory - allowing you to use native functions as if they were your own.

### Defining Reloaded-Compatible Delegates

Defining delegates to call native functions under Reloaded.Hooks is performed just like defining regular delegates - with the exception of two key things:

- Foremost, you must inform Reloaded.Hooks of the kind of function you are going to call with the use of Reloaded's own `FunctionAttribute`.

- Secondmost, you *must* also set the regular `UnmanagedFunctionAtrribute` with a calling convention of CDECL **regardless of the actual calling convention used**. Aside from that, you can use `UnmanagedFunctionAtrribute` to control marshaling and some other options as usual.

In addition, it should be noted that that there exists more than one `FunctionAttribute` , namely `X86.FunctionAttribute` and `X64.FunctionAttribute`.

Depending on your project you may wish to apply one, the other or both.

#### Examples:

Note: These examples have been lifted from real pieces of source code.
Game modifications for the original *Reloaded Mod Loader*.

**Native CDECL function:**
```csharp
/* RenderWare Graphics | Function that is step one to widescreen hacks in RenderWare games. */
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[Function(CallingConventions.Cdecl)]
public delegate void RwCameraSetViewWindow(ref RWCamera RwCamera, ref RWView view);
```

**Function with registers as parameters:**
```csharp
/// <summary>
/// Reads a ANM/HAnim stream from a .ONE archive. Returns address of a decompressed ANM file.
/// </summary>
/// <param name="fileIndex">[EAX] The index of the file inside the .ONE archive (starting with 2)</param>
/// <param name="addressToDecompressTo">[ECX] The address to which the file inside the ONE archive will be decompressed to.</param>
/// <param name="thisPointer">"This" pointer for the ONEFILE class instance.</param>
/// <returns>The address containing the read in ANM (H Anim - Character Animation) stream.</returns>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[Function(new[] { Register.eax, Register.ecx }, Register.eax, StackCleanup.Callee)]
public delegate int OneFileLoadHAnimation(int fileIndex, void* addressToDecompressTo, ref ONEFILE thisPointer);
/* Sonic Heroes */
```

*This was an example of a custom function with the following properties:*

- Two parameters (left to right) in registers `EAX` and `ECX`. 
- Return register of `EAX`. 
- "Callee" stack cleanup, i.e. the stack pointer is reset at the end of the function either via `ret X` or `add esp, X`. 

Those with some experience in reverse engineering might know that IDA would identify such function as `userpurge`.

For custom functions, under the hood Reloaded.Hooks creates CDECL/Microsoft X64 to Custom convention wrappers with its own JIT Assembler. These wrappers are actually what will be called behind the scenes as you call your native functions.

### Calling Functions

In order to create an instance of your own custom delegate from a supplied function pointer, the `Wrapper` factory classes in either (*Reloaded.Hooks.X86*) or (*Reloaded.Hooks.X64*) are used respectively. These will return you an instance of your requested function at address which you can call like a native function.

```csharp
// Based on the delegate above.
RwCameraSetViewWindow rwCameraSetViewWindow = Wrapper.Create<RwCameraSetViewWindow>(0x0064AC80);

// You may now call the delegate instance/native/game fuction like if it was your own.
rwCameraSetViewWindow(RwCamera, view);
```

Regarding the other, more complex nonstandard function seen above that has been optimized out by the compiler - nothing changes. The process is exactly the same and saves you having to write what would otherwise be custom inline assembly in the C++ world.

### Hooking Functions

Just like Reloaded.Hooks makes the calling of any regular or custom arbitrary functions involving register parameters easy, the hooking of arbitrary functions in Reloaded can also be considered a simple walk in the park through the use of the `Hook`  class.

The usage is fairly simple and mostly self-explanatory. Below is a simple example, stripped down and simplified from the original *Reloaded Mod Loader Samples*:

#### Example:

```csharp
/* Fields */
private IHook<CreateFileA> _createFileAHook; 

/* Constructor */
public SomeClass()
{
	// Get Address of Windows API function.
	IntPtr kernel32Handle = LoadLibraryW("kernel32");
	IntPtr createFileAPointer = GetProcAddress(kernel32Handle, "CreateFileA");
    
    _createFileAHook = new Hook<CreateFileA>(CreateFileAImpl, (long)createFileAPointer).Activate();
}

/* Hook Function */

/// <summary>
/// Contains the implementation of the CreateFileA hook.
/// Simply prints the file name to the console and calls + returns the original function.
/// </summary>
private static IntPtr CreateFileAImpl(string filename, FileAccess access, FileShare share, IntPtr securityAttributes, FileMode creationDisposition, FileAttributes flagsAndAttributes, IntPtr templateFile)
{
    // If statement filters out non-files such as HID devices;
    if (!filename.StartsWith(@"\\?\"))
        Bindings.PrintInfo($"[CFA] Loading File {filename}");

    // Calls the original function we hooked and returns its value.
    return _createFileAHook.OriginalFunction(filename, access, share, securityAttributes, creationDisposition, flagsAndAttributes, templateFile);
}
```

There is nothing extra you need to do such as writing your own custom inline assembler by hand for hooking functions like you may be used to doing in C++. The complicated stuff is already handled for you.

Reloaded's hooking system is very, very, very powerful under the hood; and equally complex at the same time due to aspects such as its own "mini-JIT" built in for the generation of `CDECL -> Calling Convention` and `Calling Convention -> CDECL` wrappers. That said, everything complex is abstracted from you to make your life easy 😉.

Reloaded.Hooks can also patch other common forms non-Reloaded hooks. Example use cases include hooking DirectX' `EndScene` or `Present` while the Steam Overlay is active and stacking hooks ontop of one another successfully.

#### Hooking Functions: Remarks

**A.** You may have noticed that nowhere in that text or the paragraph, the length of the hook was mentioned. Reloaded.Hooks is able to happily and easily calculate the hook length for you without requiring anything from you in return.\* 

\* That said, if you are reverse engineering and have specific need to set the length manually (*you know what you are doing*), feel free to do so, it's an overload. Just note that the minimum hook length is 6 bytes under x86, and 7 under x64. 

**B.** Just like function calling in Reloaded supports marshalling, the same is likewise true about function hooks. You can happily use some of the standard C# marshals alongside your custom marshalling (if you have any) for your hooks. Here is an example:

```csharp
/*
    Within native code this individual function would be expressed as "int PlayADX(char* fileName)",
    here thanks to marshalling we are able to simply specify it as a string.
    
    In this specific case, additionally, CharSet = CharSet.Ansi must be specified as the game from
    which this function originates from did not use the Unicode encoding that is default in C#. 
*/

[Function(CallingConventions.Cdecl)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)
public delegate int PlayADX(string fileName);
/* Sonic Heroes: Some wrapper around a CRIWare function. */
```

### Calling Function Pointers

Should you ever find yourself needing to call functions that are pointed to by a pointer whose value constantly changes, Reloaded.Hooks provides you with a simple utility class to help you alleviate the pain of constantly changing function addresses.

The utility class is simply named `FunctionPtr` and can be found at `Reloaded.Hooks.Tools` respectively - the usage is simple. Here is a small sample example:

```csharp
// 0x123456 is the address of a pointer which points to a function of type MyCustomDelegate
// MyCustomDelegate is a delegate marked with FunctionAttribute and UnmanagedFunctionPointerAttribute.
FunctionPtr<MyCustomDelegate> functionPtr = new FunctionPtr<MyCustomDelegate>(0x123456);

// Gets the address of our function (dereferences pointer).
var functionAddress = functionPtr.GetFunctionAddress(0); // Index: This class supports arrays of pointers.

// Gets the delegate to use for calling the native function and calls the function.
// You should this every time you intend to use the function pointer to call function. 
var myCustomFunction = functionPtr.GetDelegate();
myCustomFunction(1000);
```
#### Function Pointers: Performance
Regarding pointers to functions with custom calling conventions.

The class performs caching of pointers and function wrappers under the hood as the pointer points to each new address and you access it.

This means that for those custom functions whose pointers will alternate between a set number of values... Most, if not all at some point will already have a pre-prepared function wrapper ready to call.

### Function Pointers to C# Functions

No hacking adventure would be ever complete with pointers to our own functions.

Reloaded.Hooks supports this and as you'd expect, it makes life very easy for you. You can even have pointers to C# functions that will obey custom calling conventions. 

*A pointer to a C# function that will accept arbitrary registers as parameters without any custom human written ASM anywhere? Sign me up!*

Support for this functionality is provided through the use of the  `ReverseWrapper` classes, available in (*Reloaded.Hooks.X64*) and (*Reloaded.Hooks.X86*) respectively. These will return back an instance of the class, which contains a property `WrapperPointer` that can be used to call the C# function from native code.

```csharp
// Define an x86 fastcall function (via template).
// Microsoft Fastcall passes first two parameters in registers ECX, EDX and returns value in EAX.
[Function(CallingConventions.Fastcall)]
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void FastcallExample(int a, int b, int c);

/* Fields */
private ReverseFunctionWrapper<FastcallExample> _reverseFunctionWrapper;  // Pointer to C# Fastcall function.
private FastcallExample                         _functionWrapper;         // We will call our C# function through the pointer to prove.

/* Main/Init Method */
void FastcallCSharpFunctionPointerTest()
{
    // Create wrapper to make our "C# fastcall" function.
    _reverseFunctionWrapper = new ReverseWrapper<FastcallExample>(CSharpFastcallFunction);

    // To prove our "C# fastcall" function works, let's just call it like a native function.
    _functionWrapper = Wrapper.Create<FastcallExample>((long)reverseFunctionWrapper.Pointer);
    _functionWrapper(1,2,3);
}


/* Function Implementation */

/// <summary>
/// When called through the address in reverseFunctionWrapper.Pointer,
/// this function is now a "fastcall" function.
/// </summary>
/// <param name="a">This number is passed via ECX register!</param>
/// <param name="b">This number is passed via EDX register!</param>
/// <param name="c">This number is on the stack.</param>
private static void CSharpFastcallFunction(int a, int b, int c)
{
    MessageBox.Show($"{a + b + c}");
}
```