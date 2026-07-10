using System;
using System.Collections.Generic;
using Punch.CLI;
using Xunit;

namespace Punch.CLI.Tests;

public class PunchSessionTests
{
    private static PunchSession CreateSession(int targetHours = 8) =>
        new(new DaySchedule(new List<TimeBlock>()), new DateOnly(2026, 1, 15), "unused", 32, targetHours);

    [Fact]
    public void Constructor_SetsDefaults()
    {
        var session = CreateSession();

        Assert.Equal(32, session.CursorSlot);
        Assert.Equal(1, session.SelectionLength);
        Assert.Equal(0, session.ActiveField);
        Assert.Null(session.SelectedBlock);
        Assert.False(session.Editing);
        Assert.False(session.ShowHelp);
        Assert.False(session.ShowTicketSummary);
        Assert.False(session.ShowTicketPicker);
        Assert.Empty(session.Tickets);
        Assert.Equal(8, session.TargetHours);
    }

    [Fact]
    public void Constructor_HonorsExplicitTargetHours()
    {
        Assert.Equal(6, CreateSession(targetHours: 6).TargetHours);
    }

    [Fact]
    public void IsInputActive_TrueWhenNoBlockSelected()
    {
        Assert.True(CreateSession().IsInputActive);
    }

    [Fact]
    public void IsInputActive_FalseWhenBlockSelectedNotEditing()
    {
        var session = CreateSession();
        session.SelectedBlock = new TimeBlock(10, 2, "task", "");

        Assert.False(session.IsInputActive);
    }

    [Fact]
    public void IsInputActive_TrueWhenEditingSelectedBlock()
    {
        var session = CreateSession();
        session.SelectedBlock = new TimeBlock(10, 2, "task", "");
        session.Editing = true;

        Assert.True(session.IsInputActive);
    }

    [Fact]
    public void ActiveBuffer_RoutesToInputBufferWhenField0()
    {
        var session = CreateSession();
        session.ActiveField = 0;

        session.ActiveBuffer.Append("desc");

        Assert.Equal("desc", session.InputBuffer.ToString());
        Assert.Equal(0, session.TicketBuffer.Length);
    }

    [Fact]
    public void ActiveBuffer_RoutesToTicketBufferWhenField1()
    {
        var session = CreateSession();
        session.ActiveField = 1;

        session.ActiveBuffer.Append("ABC-1");

        Assert.Equal("ABC-1", session.TicketBuffer.ToString());
        Assert.Equal(0, session.InputBuffer.Length);
    }

    [Fact]
    public void ActiveCursor_GetAndSet_RouteToInputCursorWhenField0()
    {
        var session = CreateSession();
        session.ActiveField = 0;

        session.ActiveCursor = 5;

        Assert.Equal(5, session.InputCursor);
        Assert.Equal(0, session.TicketCursor);
        Assert.Equal(5, session.ActiveCursor);
    }

    [Fact]
    public void ActiveCursor_GetAndSet_RouteToTicketCursorWhenField1()
    {
        var session = CreateSession();
        session.ActiveField = 1;

        session.ActiveCursor = 3;

        Assert.Equal(3, session.TicketCursor);
        Assert.Equal(0, session.InputCursor);
        Assert.Equal(3, session.ActiveCursor);
    }

    [Fact]
    public void ResetInput_ClearsBuffersCursorsFieldAndEditing()
    {
        var session = CreateSession();
        session.InputBuffer.Append("desc");
        session.InputCursor = 4;
        session.TicketBuffer.Append("ABC-1");
        session.TicketCursor = 5;
        session.ActiveField = 1;
        session.Editing = true;

        session.ResetInput();

        Assert.Equal(0, session.InputBuffer.Length);
        Assert.Equal(0, session.InputCursor);
        Assert.Equal(0, session.TicketBuffer.Length);
        Assert.Equal(0, session.TicketCursor);
        Assert.Equal(0, session.ActiveField);
        Assert.False(session.Editing);
    }

    [Fact]
    public void ResetInput_PreservesTimelineSelection()
    {
        var session = CreateSession();
        var block = new TimeBlock(40, 2, "task", "");
        session.CursorSlot = 40;
        session.SelectionLength = 2;
        session.SelectedBlock = block;

        session.ResetInput();

        Assert.Equal(40, session.CursorSlot);
        Assert.Equal(2, session.SelectionLength);
        Assert.Same(block, session.SelectedBlock);
    }

    [Fact]
    public void Blocks_DelegatesToSchedule()
    {
        var block = new TimeBlock(10, 2, "task", "");
        var session = new PunchSession(new DaySchedule(new[] { block }), new DateOnly(2026, 1, 15), "unused", 32);

        Assert.Equal(block, Assert.Single(session.Blocks));
    }
}
