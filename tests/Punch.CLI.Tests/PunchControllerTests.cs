using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Punch.CLI;
using Spectre.Console;
using Xunit;

namespace Punch.CLI.Tests;

// Drives PunchController.HandleKey with synthetic ConsoleKeyInfo sequences and
// asserts on the resulting PunchSession/DaySchedule state. Handlers persist via
// the static PunchStorage, so the suite joins the storage collection and points
// DataDirectoryOverride at a temp dir (with tickets.txt resolving to its parent,
// mirroring LoadTicketsTests).
[Collection(StorageCollection.Name)]
public class PunchControllerTests : IDisposable
{
    private static readonly DateOnly Date = new(2026, 1, 15);

    private readonly string _tempDir;
    private readonly string _ticketsPath;

    public PunchControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "punch-controller-" + Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(dataDir);
        PunchStorage.DataDirectoryOverride = dataDir;
        _ticketsPath = PunchStorage.GetTicketsFilePath();
    }

    public void Dispose()
    {
        PunchStorage.DataDirectoryOverride = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static (PunchController Controller, PunchSession Session) Create(
        int cursorSlot = 32, int height = 40, params TimeBlock[] blocks)
    {
        var session = new PunchSession(new DaySchedule(blocks), Date, "unused", cursorSlot);
        var controller = new PunchController(session, new PunchView(new Layout("Root")), () => height);
        return (controller, session);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0', bool ctrl = false) =>
        new(ch, key, shift: false, alt: false, control: ctrl);

    private static ConsoleKeyInfo Ch(char c) => new(c, 0, shift: false, alt: false, control: false);

    private static void Type(PunchController controller, string text)
    {
        foreach (var ch in text)
            controller.HandleKey(Ch(ch));
    }

    // Selects the block starting at startSlot by arrowing left onto it from the
    // slot just past its end (the session cursor must sit there already).
    private static void SelectByLeftArrow(PunchController controller) =>
        controller.HandleKey(Key(ConsoleKey.LeftArrow));

    // --- Text input / booking ---

    [Fact]
    public void TypedChars_AppendToDescriptionBuffer()
    {
        var (controller, session) = Create();

        Type(controller, "fix bug");

        Assert.Equal("fix bug", session.InputBuffer.ToString());
        Assert.Equal(7, session.InputCursor);
    }

    [Fact]
    public void Tab_SwitchesToTicketFieldAndBack()
    {
        var (controller, session) = Create();
        Type(controller, "desc");

        controller.HandleKey(Key(ConsoleKey.Tab));
        Assert.Equal(1, session.ActiveField);
        Assert.Equal(0, session.TicketCursor);

        controller.HandleKey(Key(ConsoleKey.Tab));
        Assert.Equal(0, session.ActiveField);
        Assert.Equal(4, session.InputCursor);
    }

    [Fact]
    public void TicketField_UppercasesTypedChars()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.Tab));
        Type(controller, "abc-1");

        Assert.Equal("ABC-1", session.TicketBuffer.ToString());
    }

    [Fact]
    public void Backspace_RemovesCharBeforeCursor()
    {
        var (controller, session) = Create();
        Type(controller, "abc");

        controller.HandleKey(Key(ConsoleKey.Backspace));

        Assert.Equal("ab", session.InputBuffer.ToString());
        Assert.Equal(2, session.InputCursor);
    }

    [Fact]
    public void Backspace_AtCursorZero_IsNoOp()
    {
        var (controller, session) = Create();
        Type(controller, "abc");
        controller.HandleKey(Key(ConsoleKey.Home));

        controller.HandleKey(Key(ConsoleKey.Backspace));

        Assert.Equal("abc", session.InputBuffer.ToString());
        Assert.Equal(0, session.InputCursor);
    }

    [Fact]
    public void Delete_RemovesCharAtCursor()
    {
        var (controller, session) = Create();
        Type(controller, "abc");
        controller.HandleKey(Key(ConsoleKey.Home));

        controller.HandleKey(Key(ConsoleKey.Delete));

        Assert.Equal("bc", session.InputBuffer.ToString());
    }

    [Fact]
    public void HomeAndEnd_MoveCursorToEdges()
    {
        var (controller, session) = Create();
        Type(controller, "abc");

        controller.HandleKey(Key(ConsoleKey.Home));
        Assert.Equal(0, session.InputCursor);

        controller.HandleKey(Key(ConsoleKey.End));
        Assert.Equal(3, session.InputCursor);
    }

    [Fact]
    public void ControlChars_AreIgnored()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.F1));

        Assert.Equal(0, session.InputBuffer.Length);
    }

    [Fact]
    public void TextInput_IgnoredWhenBlockSelectedNotEditing()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);
        Assert.NotNull(session.SelectedBlock);

        Type(controller, "abc");

        Assert.Equal(0, session.InputBuffer.Length);
    }

    [Fact]
    public void Enter_WithDescription_BooksBlockSavesAndAdvances()
    {
        var (controller, session) = Create();
        Type(controller, "standup");
        controller.HandleKey(Key(ConsoleKey.UpArrow));
        controller.HandleKey(Key(ConsoleKey.UpArrow));

        controller.HandleKey(Key(ConsoleKey.Enter));

        var block = Assert.Single(session.Blocks);
        Assert.Equal(new TimeBlock(32, 3, "standup", ""), block);
        Assert.Equal(block, Assert.Single(PunchStorage.Load(Date)));
        Assert.Equal(0, session.InputBuffer.Length);
        Assert.Equal(35, session.CursorSlot);
        Assert.Null(session.SelectedBlock);
    }

    [Fact]
    public void Enter_WithEmptyDescription_DoesNothing()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.Enter));

        Assert.Empty(session.Blocks);
        Assert.Empty(PunchStorage.Load(Date));
    }

    [Fact]
    public void Enter_WhenBookingFillsEndOfDay_SelectsBlockAt95()
    {
        var (controller, session) = Create(cursorSlot: 92);
        Type(controller, "wrap up");
        for (var i = 0; i < 3; i++)
            controller.HandleKey(Key(ConsoleKey.UpArrow));

        controller.HandleKey(Key(ConsoleKey.Enter));

        Assert.Equal(95, session.CursorSlot);
        Assert.NotNull(session.SelectedBlock);
        Assert.Equal(4, session.SelectionLength);
    }

    // --- Arrows / selection ---

    [Fact]
    public void RightArrow_FreeCursor_MovesRightOne()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.RightArrow));

        Assert.Equal(33, session.CursorSlot);
    }

    [Fact]
    public void LeftArrow_FreeCursor_MovesLeftOne()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.Equal(31, session.CursorSlot);
    }

    [Fact]
    public void LeftArrow_AtSlotZero_IsNoOp()
    {
        var (controller, session) = Create(cursorSlot: 0);

        controller.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.Equal(0, session.CursorSlot);
    }

    [Fact]
    public void RightArrow_SelectionTouching96_IsNoOp()
    {
        var (controller, session) = Create(cursorSlot: 95);

        controller.HandleKey(Key(ConsoleKey.RightArrow));

        Assert.Equal(95, session.CursorSlot);
    }

    [Fact]
    public void RightArrow_OntoAdjacentBlock_SelectsIt()
    {
        var block = new TimeBlock(33, 2, "task", "");
        var (controller, session) = Create(blocks: block);

        controller.HandleKey(Key(ConsoleKey.RightArrow));

        Assert.Equal(block, session.SelectedBlock);
        Assert.Equal(33, session.CursorSlot);
        Assert.Equal(2, session.SelectionLength);
    }

    [Fact]
    public void LeftArrow_OntoAdjacentBlock_SelectsIt()
    {
        var block = new TimeBlock(30, 2, "task", "");
        var (controller, session) = Create(blocks: block);

        controller.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.Equal(block, session.SelectedBlock);
        Assert.Equal(30, session.CursorSlot);
        Assert.Equal(2, session.SelectionLength);
    }

    [Fact]
    public void RightArrow_FromSelectedBlock_LandsOnSlotAfterBlock()
    {
        var (controller, session) = Create(blocks: new TimeBlock(33, 2, "task", ""));
        controller.HandleKey(Key(ConsoleKey.RightArrow)); // select

        controller.HandleKey(Key(ConsoleKey.RightArrow)); // move past

        Assert.Null(session.SelectedBlock);
        Assert.Equal(35, session.CursorSlot);
        Assert.Equal(1, session.SelectionLength);
    }

    [Fact]
    public void RightArrow_FromSelectedBlock_SnapsToAdjacentBlock()
    {
        var second = new TimeBlock(35, 3, "next", "");
        var (controller, session) = Create(blocks: new[] { new TimeBlock(33, 2, "task", ""), second });
        controller.HandleKey(Key(ConsoleKey.RightArrow)); // select first

        controller.HandleKey(Key(ConsoleKey.RightArrow)); // snap to second

        Assert.Equal(second, session.SelectedBlock);
        Assert.Equal(35, session.CursorSlot);
        Assert.Equal(3, session.SelectionLength);
    }

    [Fact]
    public void LeftArrow_FromSelectedBlock_LandsBeforeBlock()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);

        controller.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.Null(session.SelectedBlock);
        Assert.Equal(9, session.CursorSlot);
    }

    [Fact]
    public void LeftArrow_FromBlockAtSlotZero_IsNoOp()
    {
        var block = new TimeBlock(0, 2, "task", "");
        var (controller, session) = Create(cursorSlot: 2, blocks: block);
        SelectByLeftArrow(controller);
        Assert.Equal(block, session.SelectedBlock);

        controller.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.Equal(block, session.SelectedBlock);
        Assert.Equal(0, session.CursorSlot);
    }

    [Fact]
    public void Arrows_WhileTyping_MoveTextCursorNotTimeline()
    {
        var (controller, session) = Create();
        Type(controller, "ab");

        controller.HandleKey(Key(ConsoleKey.LeftArrow));
        Assert.Equal(1, session.InputCursor);
        Assert.Equal(32, session.CursorSlot);

        controller.HandleKey(Key(ConsoleKey.RightArrow));
        Assert.Equal(2, session.InputCursor);
        Assert.Equal(32, session.CursorSlot);
    }

    [Fact]
    public void Arrow_DuringEdit_AbandonsEditAndRefillsSlots()
    {
        var (controller, session) = Create(cursorSlot: 35, blocks: new TimeBlock(33, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));
        Assert.True(session.Editing);
        Assert.True(session.Schedule.IsFree(33));

        // Clear the pre-filled label so the arrow moves the timeline, not the text cursor.
        for (var i = 0; i < 4; i++)
            controller.HandleKey(Key(ConsoleKey.Backspace));
        controller.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.False(session.Editing);
        Assert.False(session.Schedule.IsFree(33));
        Assert.Equal(0, session.InputBuffer.Length);
    }

    [Fact]
    public void Arrow_WithEmptyActiveField_ClearsPendingInput()
    {
        var (controller, session) = Create();
        Type(controller, "abc");
        controller.HandleKey(Key(ConsoleKey.Tab)); // ticket field is empty

        controller.HandleKey(Key(ConsoleKey.RightArrow));

        Assert.Equal(0, session.InputBuffer.Length);
        Assert.Equal(0, session.ActiveField);
        Assert.Equal(33, session.CursorSlot);
    }

    // --- Grow / shrink ---

    [Fact]
    public void UpArrow_FreeCursor_GrowsSelection()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.UpArrow));

        Assert.Equal(2, session.SelectionLength);
    }

    [Fact]
    public void UpArrow_BlockedByOccupiedSlot_DoesNotGrow()
    {
        var (controller, session) = Create(blocks: new TimeBlock(33, 1, "task", ""));

        controller.HandleKey(Key(ConsoleKey.UpArrow));

        Assert.Equal(1, session.SelectionLength);
    }

    [Fact]
    public void UpArrow_At96Boundary_DoesNotGrow()
    {
        var (controller, session) = Create(cursorSlot: 95);

        controller.HandleKey(Key(ConsoleKey.UpArrow));

        Assert.Equal(1, session.SelectionLength);
    }

    [Fact]
    public void DownArrow_ShrinksSelection_StopsAtOne()
    {
        var (controller, session) = Create();
        controller.HandleKey(Key(ConsoleKey.UpArrow));
        Assert.Equal(2, session.SelectionLength);

        controller.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, session.SelectionLength);

        controller.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, session.SelectionLength);
    }

    [Fact]
    public void UpArrow_OnSelectedBlockNotEditing_IsNoOp()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);

        controller.HandleKey(Key(ConsoleKey.UpArrow));

        Assert.Equal(2, session.SelectionLength);
    }

    [Fact]
    public void UpArrow_WhileEditing_GrowsWhenNextSlotFree()
    {
        var (controller, session) = Create(cursorSlot: 35, blocks: new TimeBlock(33, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));

        controller.HandleKey(Key(ConsoleKey.UpArrow));

        Assert.Equal(3, session.SelectionLength);
    }

    [Fact]
    public void UpArrow_WhileEditing_BlockedByNeighbor_DoesNotGrow()
    {
        var (controller, session) = Create(cursorSlot: 32,
            blocks: new[] { new TimeBlock(33, 2, "task", ""), new TimeBlock(35, 1, "next", "") });
        controller.HandleKey(Key(ConsoleKey.RightArrow)); // select first
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));

        controller.HandleKey(Key(ConsoleKey.UpArrow));

        Assert.Equal(2, session.SelectionLength);
    }

    [Fact]
    public void DownArrow_WhileEditing_ShrinksToMinOne()
    {
        var (controller, session) = Create(cursorSlot: 35, blocks: new TimeBlock(33, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));

        controller.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, session.SelectionLength);

        controller.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, session.SelectionLength);
    }

    // --- Edit flow (Ctrl+E) ---

    [Fact]
    public void CtrlE_OnSelectedBlock_EntersEditModeWithBuffersLoaded()
    {
        var (controller, session) = Create(cursorSlot: 35, blocks: new TimeBlock(33, 2, "task", "ABC-1"));
        SelectByLeftArrow(controller);

        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));

        Assert.True(session.Editing);
        Assert.Equal("task", session.InputBuffer.ToString());
        Assert.Equal(4, session.InputCursor);
        Assert.Equal("ABC-1", session.TicketBuffer.ToString());
        Assert.Equal(5, session.TicketCursor);
        Assert.Equal(0, session.ActiveField);
        Assert.Equal(2, session.SelectionLength);
        Assert.True(session.Schedule.IsFree(33));
    }

    [Fact]
    public void CtrlE_WithoutSelection_IsNoOp()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));

        Assert.False(session.Editing);
    }

    [Fact]
    public void CtrlE_WhileAlreadyEditing_IsNoOp()
    {
        var (controller, session) = Create(cursorSlot: 35, blocks: new TimeBlock(33, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));
        Type(controller, "2");

        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));

        Assert.True(session.Editing);
        Assert.Equal("task2", session.InputBuffer.ToString());
    }

    [Fact]
    public void Enter_DuringEdit_CommitsLabelTicketAndResize()
    {
        var (controller, session) = Create(cursorSlot: 35, blocks: new TimeBlock(33, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));
        Type(controller, "2");
        controller.HandleKey(Key(ConsoleKey.Tab));
        Type(controller, "xyz-9");
        controller.HandleKey(Key(ConsoleKey.DownArrow)); // shrink 2 -> 1

        controller.HandleKey(Key(ConsoleKey.Enter));

        var expected = new TimeBlock(33, 1, "task2", "XYZ-9");
        Assert.Equal(expected, Assert.Single(session.Blocks));
        Assert.Equal(expected, Assert.Single(PunchStorage.Load(Date)));
        Assert.True(session.Schedule.IsFree(34));
        Assert.False(session.Editing);
    }

    [Fact]
    public void Enter_DuringEdit_WithEmptiedLabel_DoesNotCommit()
    {
        var original = new TimeBlock(33, 2, "task", "");
        var (controller, session) = Create(cursorSlot: 35, blocks: original);
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));
        for (var i = 0; i < 4; i++)
            controller.HandleKey(Key(ConsoleKey.Backspace));

        controller.HandleKey(Key(ConsoleKey.Enter));

        Assert.True(session.Editing);
        Assert.Equal(original, Assert.Single(session.Blocks));
    }

    // --- Delete confirm (Ctrl+D) ---

    [Fact]
    public void CtrlD_WithSelection_ArmsDeleteConfirm()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);

        controller.HandleKey(Key(ConsoleKey.D, ctrl: true));

        Assert.True(controller.IsConfirmingDelete);
        Assert.Single(session.Blocks);
    }

    [Fact]
    public void CtrlD_ThenD_DeletesBlockSavesAndClearsSelection()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        PunchStorage.Save(Date, session.Blocks);
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.D, ctrl: true));

        controller.HandleKey(Key(ConsoleKey.D, 'd'));

        Assert.Empty(session.Blocks);
        Assert.Empty(PunchStorage.Load(Date));
        Assert.Null(session.SelectedBlock);
        Assert.Equal(1, session.SelectionLength);
        Assert.False(controller.IsConfirmingDelete);
    }

    [Fact]
    public void CtrlD_ThenOtherKey_CancelsWithoutDeleting()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.D, ctrl: true));

        controller.HandleKey(Key(ConsoleKey.X, 'x'));

        Assert.Single(session.Blocks);
        Assert.False(controller.IsConfirmingDelete);
        Assert.Equal(0, session.InputBuffer.Length); // cancel key is swallowed
    }

    [Fact]
    public void CtrlD_WithoutSelection_DoesNotArm()
    {
        var (controller, _) = Create();

        controller.HandleKey(Key(ConsoleKey.D, ctrl: true));

        Assert.False(controller.IsConfirmingDelete);
    }

    // --- Quit confirm (Ctrl+Q) ---

    [Fact]
    public void CtrlQ_ArmsQuitConfirm()
    {
        var (controller, _) = Create();

        Assert.False(controller.HandleKey(Key(ConsoleKey.Q, ctrl: true)));
        Assert.True(controller.IsConfirmingQuit);
    }

    [Fact]
    public void CtrlQ_ThenQ_SignalsQuit()
    {
        var (controller, _) = Create();
        controller.HandleKey(Key(ConsoleKey.Q, ctrl: true));

        Assert.True(controller.HandleKey(Key(ConsoleKey.Q, 'q')));
    }

    [Fact]
    public void CtrlQ_ThenOtherKey_CancelsAndSwallowsKey()
    {
        var (controller, session) = Create();
        controller.HandleKey(Key(ConsoleKey.Q, ctrl: true));

        Assert.False(controller.HandleKey(Key(ConsoleKey.X, 'x')));
        Assert.False(controller.IsConfirmingQuit);
        Assert.Equal(0, session.InputBuffer.Length);
    }

    [Fact]
    public void CtrlQ_TakesPriorityOverHelpOverlay()
    {
        var (controller, session) = Create();
        controller.HandleKey(Ch('?'));
        Assert.True(session.ShowHelp);

        controller.HandleKey(Key(ConsoleKey.Q, ctrl: true));

        Assert.True(controller.IsConfirmingQuit);
        Assert.True(session.ShowHelp);
        Assert.True(controller.HandleKey(Key(ConsoleKey.Q, 'q')));
    }

    // --- Help overlay ---

    [Fact]
    public void QuestionMark_WithEmptyBuffers_TogglesHelpOn()
    {
        var (controller, session) = Create();

        controller.HandleKey(Ch('?'));

        Assert.True(session.ShowHelp);
    }

    [Fact]
    public void AnyKey_WhileHelpShown_DismissesHelpAndIsSwallowed()
    {
        var (controller, session) = Create();
        controller.HandleKey(Ch('?'));

        controller.HandleKey(Ch('x'));

        Assert.False(session.ShowHelp);
        Assert.Equal(0, session.InputBuffer.Length);
    }

    [Fact]
    public void QuestionMark_WithTextInBuffer_IsTypedIntoDescription()
    {
        var (controller, session) = Create();
        Type(controller, "why");

        controller.HandleKey(Ch('?'));

        Assert.False(session.ShowHelp);
        Assert.Equal("why?", session.InputBuffer.ToString());
    }

    [Fact]
    public void QuestionMark_OnSelectedBlock_TogglesHelp()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);

        controller.HandleKey(Ch('?'));

        Assert.True(session.ShowHelp);
    }

    // --- Ticket picker (F4 / Ctrl+P) ---

    private void SeedTickets() =>
        File.WriteAllText(_ticketsPath, "ABC-1\tFirst ticket\nDEF-2\tSecond ticket\n");

    private (PunchController Controller, PunchSession Session) CreateWithPickerOpen()
    {
        SeedTickets();
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.F4));
        Assert.True(session.ShowTicketPicker);
        return (controller, session);
    }

    [Fact]
    public void F4_WithSelectedBlock_OpensPickerAndLoadsTickets()
    {
        var (_, session) = CreateWithPickerOpen();

        Assert.Equal(0, session.TicketPickerCursor);
        Assert.Equal(2, session.Tickets.Count);
        Assert.Equal("ABC-1", session.Tickets[0].Ticket);
    }

    [Fact]
    public void CtrlP_WithSelectedBlock_OpensPicker()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);

        controller.HandleKey(Key(ConsoleKey.P, ctrl: true));

        Assert.True(session.ShowTicketPicker);
    }

    [Fact]
    public void F4_WithoutSelection_DoesNotOpenPicker()
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(ConsoleKey.F4));

        Assert.False(session.ShowTicketPicker);
    }

    [Fact]
    public void F4_WhileEditing_DoesNotOpenPicker()
    {
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.E, ctrl: true));

        controller.HandleKey(Key(ConsoleKey.F4));

        Assert.False(session.ShowTicketPicker);
    }

    [Fact]
    public void PickerArrows_MoveCursorWithClamping()
    {
        var (controller, session) = CreateWithPickerOpen();

        controller.HandleKey(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, session.TicketPickerCursor);

        controller.HandleKey(Key(ConsoleKey.DownArrow)); // clamp at last
        Assert.Equal(1, session.TicketPickerCursor);

        controller.HandleKey(Key(ConsoleKey.UpArrow));
        controller.HandleKey(Key(ConsoleKey.UpArrow)); // clamp at zero
        Assert.Equal(0, session.TicketPickerCursor);
    }

    [Fact]
    public void PickerEnter_AssignsPickedTicketSavesAndCloses()
    {
        var (controller, session) = CreateWithPickerOpen();
        controller.HandleKey(Key(ConsoleKey.DownArrow));

        controller.HandleKey(Key(ConsoleKey.Enter));

        var expected = new TimeBlock(10, 2, "task", "DEF-2");
        Assert.Equal(expected, Assert.Single(session.Blocks));
        Assert.Equal(expected, session.SelectedBlock);
        Assert.Equal(expected, Assert.Single(PunchStorage.Load(Date)));
        Assert.False(session.ShowTicketPicker);
        Assert.False(session.Schedule.IsFree(10));
    }

    [Theory]
    [InlineData(ConsoleKey.Escape, false)]
    [InlineData(ConsoleKey.F4, false)]
    [InlineData(ConsoleKey.P, true)]
    public void PickerCancelKeys_CloseWithoutAssigning(ConsoleKey key, bool ctrl)
    {
        var (controller, session) = CreateWithPickerOpen();

        controller.HandleKey(Key(key, ctrl: ctrl));

        Assert.False(session.ShowTicketPicker);
        Assert.Equal("", Assert.Single(session.Blocks).Ticket);
    }

    [Fact]
    public void Picker_SwallowsUnrelatedKeys()
    {
        var (controller, session) = CreateWithPickerOpen();

        controller.HandleKey(Ch('x'));

        Assert.True(session.ShowTicketPicker);
        Assert.Equal(0, session.InputBuffer.Length);
    }

    [Fact]
    public void PickerEnter_WithEmptyTicketList_JustCloses()
    {
        // No tickets.txt seeded: the picker opens with zero entries.
        var (controller, session) = Create(cursorSlot: 12, blocks: new TimeBlock(10, 2, "task", ""));
        SelectByLeftArrow(controller);
        controller.HandleKey(Key(ConsoleKey.F4));
        Assert.Empty(session.Tickets);

        controller.HandleKey(Key(ConsoleKey.Enter));

        Assert.False(session.ShowTicketPicker);
        Assert.Equal("", Assert.Single(session.Blocks).Ticket);
    }

    // --- Ticket summary (F3 / Ctrl+T) ---

    [Theory]
    [InlineData(ConsoleKey.F3, false)]
    [InlineData(ConsoleKey.T, true)]
    public void SummaryOpenKeys_ShowTicketSummary(ConsoleKey key, bool ctrl)
    {
        var (controller, session) = Create();

        controller.HandleKey(Key(key, ctrl: ctrl));

        Assert.True(session.ShowTicketSummary);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape, false)]
    [InlineData(ConsoleKey.F3, false)]
    [InlineData(ConsoleKey.T, true)]
    public void SummaryCloseKeys_HideTicketSummary(ConsoleKey key, bool ctrl)
    {
        var (controller, session) = Create();
        controller.HandleKey(Key(ConsoleKey.F3));

        controller.HandleKey(Key(key, ctrl: ctrl));

        Assert.False(session.ShowTicketSummary);
    }

    [Fact]
    public void Summary_SwallowsOtherKeys()
    {
        var (controller, session) = Create();
        controller.HandleKey(Key(ConsoleKey.F3));

        controller.HandleKey(Ch('x'));

        Assert.True(session.ShowTicketSummary);
        Assert.Equal(0, session.InputBuffer.Length);
    }

    // --- Scrolling / viewport ---

    private static TimeBlock[] SixBlocks() =>
        Enumerable.Range(0, 6).Select(i => new TimeBlock(i * 2, 2, $"task{i}", "")).ToArray();

    [Fact]
    public void PageUp_DecrementsOffsetClampedAtZero()
    {
        var (controller, session) = Create(cursorSlot: 40, height: 15, blocks: SixBlocks());
        session.LogScrollOffset = 3;

        controller.HandleKey(Key(ConsoleKey.PageUp));

        Assert.Equal(0, session.LogScrollOffset);
    }

    [Fact]
    public void PageDown_IncrementsOffsetClampedToViewportMax()
    {
        // height 15 -> viewHeight 3 -> maxOff = 6 blocks - 2 = 4.
        var (controller, session) = Create(cursorSlot: 40, height: 15, blocks: SixBlocks());

        controller.HandleKey(Key(ConsoleKey.PageDown));

        Assert.Equal(4, session.LogScrollOffset);
    }

    [Fact]
    public void AutoScroll_SelectionBelowViewport_ScrollsDown()
    {
        // height 15 -> visHeight 2; selecting the last block (index 5) scrolls to 5-2+1=4.
        var (controller, session) = Create(cursorSlot: 12, height: 15, blocks: SixBlocks());
        SelectByLeftArrow(controller); // selects block at slot 10 (last)

        Assert.Equal(10, session.SelectedBlock!.StartSlot);
        Assert.Equal(4, session.LogScrollOffset);
    }

    [Fact]
    public void AutoScroll_SelectionAboveOffset_ScrollsUp()
    {
        var (controller, session) = Create(cursorSlot: 12, height: 15, blocks: SixBlocks());
        SelectByLeftArrow(controller); // scrolled to bottom (offset 4)
        // Walk the selection back to the first block.
        for (var i = 0; i < 5; i++)
            controller.HandleKey(Key(ConsoleKey.LeftArrow));

        Assert.Equal(0, session.SelectedBlock!.StartSlot);
        Assert.Equal(0, session.LogScrollOffset);
    }

    [Fact]
    public void ClampScrollOffset_ReinsInExcessiveOffset()
    {
        var (controller, session) = Create(cursorSlot: 40, height: 15, blocks: SixBlocks());
        session.LogScrollOffset = 500;

        controller.HandleKey(Key(ConsoleKey.Home));

        Assert.Equal(4, session.LogScrollOffset);
    }
}
