p = 'generator/GstSharp.Generator/Emit/CallableRenderer.cs'
s = open(p, encoding='utf-8').read()

# The summary of InstanceLocal ended up over the adopted note; put it back where
# it belongs, ahead of the constant it describes.
old = """    /// <summary>The local that holds the raw handle of the instance.</summary>
    /// <summary>What a handler is told about an argument whose reference it is handed.</summary>"""
new = """    /// <summary>What a handler is told about an argument whose reference it is handed.</summary>"""
assert old in s
s = s.replace(old, new, 1)

old = """    private const string InstanceLocal = "instanceHandle";"""
new = """    /// <summary>The local that holds the raw handle of the instance.</summary>
    private const string InstanceLocal = "instanceHandle";"""
assert old in s
s = s.replace(old, new, 1)

# The same for the two predicates, whose summaries slid onto each other.
old = """    /// <summary>
    /// Tests whether a callback argument is a handle whose reference travels
    /// into the handler.
    /// </summary>
    /// <param name="argument">The argument to test.</param>
    /// <returns><see langword="true"/> when the trampoline adopts it.</returns>
    /// <summary>
    /// Tests whether a callback argument is a mini object the handler only
    /// borrows for the invocation.
    /// </summary>
    /// <param name="argument">The argument to test.</param>
    /// <returns><see langword="true"/> when the wrapper takes no reference.</returns>
    private static bool BorrowsMiniObject(ArgumentPlan argument) =>"""
new = """    /// <summary>
    /// Tests whether a callback argument is a mini object the handler only
    /// borrows for the invocation.
    /// </summary>
    /// <param name="argument">The argument to test.</param>
    /// <returns><see langword="true"/> when the wrapper takes no reference.</returns>
    private static bool BorrowsMiniObject(ArgumentPlan argument) =>"""
assert old in s
s = s.replace(old, new, 1)

old = """    private static bool IsAdopted(ArgumentPlan argument) =>"""
new = """    /// <summary>
    /// Tests whether a callback argument is a handle whose reference travels
    /// into the handler.
    /// </summary>
    /// <param name="argument">The argument to test.</param>
    /// <returns><see langword="true"/> when the trampoline adopts it.</returns>
    private static bool IsAdopted(ArgumentPlan argument) =>"""
assert old in s
s = s.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='\n').write(s)

p = 'samples/AotSmoke/Program.cs'
s = open(p, encoding='utf-8').read()
old = """    /// <remarks>
    /// The pair is generic over the managed type of the property, and generic
    /// code is where ILC has the most room to leave something behind: the
    /// enumeration read goes through <c>Enum.ToObject</c> over a type argument,
    /// and the wrapper read goes through the type registry with one. Neither is
    /// a build warning when it fails, so both are asked here.
    /// </remarks>
    /// <summary>
    /// Drives a managed chain function, whose trampoline recovers the delegate
    /// from the pad it is called with rather than from a user data pointer.
    /// </summary>
    /// <returns>Whether the buffer reached the handler.</returns>
    private static bool RunPadChainFunction()"""
new = """    /// <summary>
    /// Drives a managed chain function, whose trampoline recovers the delegate
    /// from the pad it is called with rather than from a user data pointer.
    /// </summary>
    /// <returns>Whether the buffer reached the handler.</returns>
    private static bool RunPadChainFunction()"""
assert old in s
s = s.replace(old, new, 1)

old = """    /// <returns>
    /// <see langword="true"/> when every property answered what was written to
    /// it.
    /// </returns>
    private static bool RunPropertiesByName()"""
new = """    /// <returns>
    /// <see langword="true"/> when every property answered what was written to
    /// it.
    /// </returns>
    /// <remarks>
    /// The pair is generic over the managed type of the property, and generic
    /// code is where ILC has the most room to leave something behind: the
    /// enumeration read goes through <c>Enum.ToObject</c> over a type argument,
    /// and the wrapper read goes through the type registry with one. Neither is
    /// a build warning when it fails, so both are asked here.
    /// </remarks>
    private static bool RunPropertiesByName()"""
assert old in s
s = s.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ok')
