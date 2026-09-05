using System.Runtime.CompilerServices;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The byte layout of the GES class struct mirrors, against the numbers a C
/// compiler measured.
/// </summary>
/// <remarks>
/// <para>
/// The GES class structs are the first of the allowlist that keep their later
/// additions inside a <c>&lt;union&gt;</c>, and a union is the one shape whose
/// size a mirror cannot read off the members it spells: the record the mirror
/// lays out is the smaller member every time, and the reserve behind it is what
/// the C compiler measured. A mirror that stopped at the end of the record
/// would be a hundred and sixty bytes short on <c>GESClipClass</c>, and every
/// class derived from it would write its slots into the reserve of its parent.
/// </para>
/// <para>
/// The expected numbers were taken from a C program compiled against the
/// installed headers, not from this binding, which is what makes them a
/// statement and not a restatement. <see cref="AbiProbeTests"/> checks the same
/// mirrors against the running library; this test needs no library at all, so
/// the layout is pinned even where GES is not installed.
/// </para>
/// </remarks>
public sealed class GesClassStructLayoutTests
{
    /// <summary>
    /// The size of every GES mirror, in the order the chain builds them.
    /// </summary>
    /// <remarks>
    /// <c>GESSourceClipClass</c> adds nothing but its reserve, and
    /// <c>GESAudioSourceClass</c> and <c>GESVideoSourceClass</c> are the same
    /// size by coincidence: the audio one reserves four pointers where the
    /// video one spends them.
    /// </remarks>
    [Fact]
    public void EveryMirrorHasTheSizeTheCompilerMeasured()
    {
        Assert.Equal(408, Unsafe.SizeOf<GES.TimelineElementClassRaw>());
        Assert.Equal(632, Unsafe.SizeOf<GES.ContainerClassRaw>());
        Assert.Equal(808, Unsafe.SizeOf<GES.ClipClassRaw>());
        Assert.Equal(840, Unsafe.SizeOf<GES.SourceClipClassRaw>());
        Assert.Equal(624, Unsafe.SizeOf<GES.TrackElementClassRaw>());
        Assert.Equal(656, Unsafe.SizeOf<GES.SourceClassRaw>());
        Assert.Equal(696, Unsafe.SizeOf<GES.VideoSourceClassRaw>());
        Assert.Equal(696, Unsafe.SizeOf<GES.AudioSourceClassRaw>());
    }

    /// <summary>
    /// The offsets a subclass writes its slots at, which is what a union that
    /// moved would take with it.
    /// </summary>
    /// <remarks>
    /// A size alone would not catch a member moving inside a union, so the
    /// members after and inside every union are named here one by one.
    /// </remarks>
    [Fact]
    public void EverySlotSitsWhereTheCompilerPutIt()
    {
        Assert.Equal(624, GES.SourceClassRaw.SelectPadOffset);
        Assert.Equal(632, GES.SourceClassRaw.CreateSourceOffset);
        Assert.Equal(632, GES.ClipClassRaw.CreateTrackElementOffset);
        Assert.Equal(640, GES.ClipClassRaw.CreateTrackElementsOffset);
        Assert.Equal(416, GES.TrackElementClassRaw.CreateGnlObjectOffset);
        Assert.Equal(424, GES.TrackElementClassRaw.CreateElementOffset);
    }

    /// <summary>
    /// The members the GES unions hold, which the mirror lays out for their
    /// width and gives no managed surface.
    /// </summary>
    /// <remarks>
    /// The <c>create_source</c> of <c>GESVideoSourceClass</c> and of
    /// <c>GESAudioSourceClass</c> is a second field of that name, in front of
    /// the union and behind the one <c>GESSourceClass</c> declares; nothing
    /// calls it, and it is here because the union behind it would move if it
    /// were dropped.
    /// </remarks>
    [Fact]
    public void EveryUnionMemberSitsWhereTheCompilerPutIt()
    {
        GES.ClipClassRaw clip = default;
        Assert.Equal(648, OffsetOf(ref clip, ref clip.CanAddEffects));

        GES.TrackElementClassRaw trackElement = default;
        Assert.Equal(464, OffsetOf(ref trackElement, ref trackElement.DefaultHasInternalSource));
        Assert.Equal(468, OffsetOf(ref trackElement, ref trackElement.DefaultTrackType));

        GES.VideoSourceClassRaw videoSource = default;
        Assert.Equal(656, OffsetOf(ref videoSource, ref videoSource.CreateSource));
        Assert.Equal(664, OffsetOf(ref videoSource, ref videoSource.DisableScaleInCompositor));
        Assert.Equal(672, OffsetOf(ref videoSource, ref videoSource.NeedsConverters));
        Assert.Equal(680, OffsetOf(ref videoSource, ref videoSource.GetNaturalSize));
        Assert.Equal(688, OffsetOf(ref videoSource, ref videoSource.CreateFilters));

        GES.AudioSourceClassRaw audioSource = default;
        Assert.Equal(656, OffsetOf(ref audioSource, ref audioSource.CreateSource));
    }

    /// <summary>Measures where one member of a mirror sits.</summary>
    /// <typeparam name="TClass">The mirror.</typeparam>
    /// <typeparam name="TMember">The type of the member.</typeparam>
    /// <param name="origin">The start of the mirror.</param>
    /// <param name="member">The member to measure.</param>
    /// <returns>The offset in bytes.</returns>
    /// <remarks>
    /// <c>Marshal.OffsetOf</c> is no use here: a mirror holds an inline array,
    /// which has no marshalling layout, and the members that matter are not all
    /// pointers, which is all <c>ClassSlot.OffsetOf</c> measures.
    /// </remarks>
    private static int OffsetOf<TClass, TMember>(ref TClass origin, ref TMember member)
        where TClass : struct
        where TMember : struct =>
        (int)Unsafe.ByteOffset(
            ref Unsafe.As<TClass, byte>(ref origin),
            ref Unsafe.As<TMember, byte>(ref member));
}
