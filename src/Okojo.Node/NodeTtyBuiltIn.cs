using System.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeTtyBuiltIn(
    NodeRuntime runtime,
    NodeEventsBuiltIn eventsBuiltIn,
    TextWriter stdoutWriter,
    TextWriter stderrWriter,
    bool stdinIsTty,
    bool stdoutIsTty,
    bool stderrIsTty,
    int? stdoutColumns,
    int? stdoutRows,
    int? stderrColumns,
    int? stderrRows)
{
    private const int TtyModuleIsAttySlot = 0;

    private const int StreamWriteSlot = 0;
    private const int StreamCursorToSlot = 1;
    private const int StreamMoveCursorSlot = 2;
    private const int StreamClearLineSlot = 3;
    private const int StreamClearScreenDownSlot = 4;
    private const int StreamFdSlot = 5;
    private const int StreamIsTtySlot = 6;
    private const int StreamColumnsSlot = 7;
    private const int StreamRowsSlot = 8;

    private const int InputReadSlot = 0;
    private const int InputSetEncodingSlot = 1;
    private const int InputSetRawModeSlot = 2;
    private const int InputRefSlot = 3;
    private const int InputUnrefSlot = 4;
    private const int InputFdSlot = 5;
    private const int InputIsTtySlot = 6;
    private int atomClearLine = -1;
    private int atomClearScreenDown = -1;
    private int atomColumns = -1;
    private int atomCursorTo = -1;
    private int atomFd = -1;

    private int atomIsAtty = -1;
    private int atomIsTty = -1;
    private int atomMoveCursor = -1;
    private int atomRead = -1;
    private int atomRef = -1;
    private int atomRows = -1;
    private int atomSetEncoding = -1;
    private int atomSetRawMode = -1;
    private int atomUnref = -1;
    private int atomWrite = -1;
    private StaticNamedPropertyLayout? inputShape;
    private JsUserDataObject<StreamState>? stderrObject;
    private JsUserDataObject<InputState>? stdinObject;
    private JsUserDataObject<StreamState>? stdoutObject;
    private StaticNamedPropertyLayout? streamShape;

    private JsPlainObject? ttyModule;
    private StaticNamedPropertyLayout? ttyModuleShape;

    public JsPlainObject GetTtyModule()
    {
        if (ttyModule is not null)
            return ttyModule;

        var realm = runtime.MainRealm;
        var shape = ttyModuleShape ??= CreateTtyModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(TtyModuleIsAttySlot, JsValue.FromObject(CreateIsAttyFunction(realm)));
        ttyModule = module;
        return module;
    }

    public JsObject GetStdoutObject()
    {
        return stdoutObject ??=
            CreateStreamObject(runtime.MainRealm, stdoutWriter, 1, stdoutIsTty, stdoutColumns, stdoutRows);
    }

    public JsObject GetStderrObject()
    {
        return stderrObject ??=
            CreateStreamObject(runtime.MainRealm, stderrWriter, 2, stderrIsTty, stderrColumns, stderrRows);
    }

    public JsObject GetStdinObject()
    {
        return stdinObject ??= CreateInputObject(runtime.MainRealm, 0, stdinIsTty);
    }

    private JsUserDataObject<StreamState> CreateStreamObject(
        JsRealm realm,
        TextWriter writer,
        int fd,
        bool isTty,
        int? columns,
        int? rows)
    {
        var shape = streamShape ??= CreateStreamShape(realm);
        var stream = new JsUserDataObject<StreamState>(shape);
        stream.Prototype = eventsBuiltIn.GetPrototypeObject();
        eventsBuiltIn.InitializeEmitterReceiver(realm, stream);
        stream.UserData = new(writer, fd, isTty, columns, rows);
        stream.SetNamedSlotUnchecked(StreamWriteSlot, JsValue.FromObject(CreateWriteFunction(realm)));
        stream.SetNamedSlotUnchecked(StreamCursorToSlot, JsValue.FromObject(CreateCursorToFunction(realm)));
        stream.SetNamedSlotUnchecked(StreamMoveCursorSlot, JsValue.FromObject(CreateMoveCursorFunction(realm)));
        stream.SetNamedSlotUnchecked(StreamClearLineSlot, JsValue.FromObject(CreateClearLineFunction(realm)));
        stream.SetNamedSlotUnchecked(StreamClearScreenDownSlot,
            JsValue.FromObject(CreateClearScreenDownFunction(realm)));
        stream.SetNamedSlotUnchecked(StreamFdSlot, JsValue.FromInt32(fd));
        stream.SetNamedSlotUnchecked(StreamIsTtySlot, isTty ? JsValue.True : JsValue.False);
        stream.SetNamedSlotUnchecked(StreamColumnsSlot,
            columns.HasValue ? JsValue.FromInt32(columns.Value) : JsValue.Undefined);
        stream.SetNamedSlotUnchecked(StreamRowsSlot, rows.HasValue ? JsValue.FromInt32(rows.Value) : JsValue.Undefined);
        return stream;
    }

    private JsUserDataObject<InputState> CreateInputObject(JsRealm realm, int fd, bool isTty)
    {
        var shape = inputShape ??= CreateInputShape(realm);
        var input = new JsUserDataObject<InputState>(shape);
        input.Prototype = eventsBuiltIn.GetPrototypeObject();
        eventsBuiltIn.InitializeEmitterReceiver(realm, input);
        input.UserData = new(fd, isTty);
        input.SetNamedSlotUnchecked(InputReadSlot, JsValue.FromObject(CreateReadFunction(realm)));
        input.SetNamedSlotUnchecked(InputSetEncodingSlot, JsValue.FromObject(CreateSetEncodingFunction(realm)));
        input.SetNamedSlotUnchecked(InputSetRawModeSlot, JsValue.FromObject(CreateSetRawModeFunction(realm)));
        input.SetNamedSlotUnchecked(InputRefSlot, JsValue.FromObject(CreateRefFunction(realm)));
        input.SetNamedSlotUnchecked(InputUnrefSlot, JsValue.FromObject(CreateUnrefFunction(realm)));
        input.SetNamedSlotUnchecked(InputFdSlot, JsValue.FromInt32(fd));
        input.SetNamedSlotUnchecked(InputIsTtySlot, isTty ? JsValue.True : JsValue.False);
        return input;
    }

    private StaticNamedPropertyLayout CreateTtyModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomIsAtty, JsShapePropertyFlags.Open, out var isAttyInfo);
        Debug.Assert(isAttyInfo.Slot == TtyModuleIsAttySlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateStreamShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomWrite, JsShapePropertyFlags.Open, out var writeInfo);
        shape = shape.GetOrAddTransition(atomCursorTo, JsShapePropertyFlags.Open, out var cursorToInfo);
        shape = shape.GetOrAddTransition(atomMoveCursor, JsShapePropertyFlags.Open, out var moveCursorInfo);
        shape = shape.GetOrAddTransition(atomClearLine, JsShapePropertyFlags.Open, out var clearLineInfo);
        shape = shape.GetOrAddTransition(atomClearScreenDown, JsShapePropertyFlags.Open, out var clearScreenDownInfo);
        shape = shape.GetOrAddTransition(atomFd, JsShapePropertyFlags.Open, out var fdInfo);
        shape = shape.GetOrAddTransition(atomIsTty, JsShapePropertyFlags.Open, out var isTtyInfo);
        shape = shape.GetOrAddTransition(atomColumns, JsShapePropertyFlags.Open, out var columnsInfo);
        shape = shape.GetOrAddTransition(atomRows, JsShapePropertyFlags.Open, out var rowsInfo);
        Debug.Assert(writeInfo.Slot == StreamWriteSlot);
        Debug.Assert(cursorToInfo.Slot == StreamCursorToSlot);
        Debug.Assert(moveCursorInfo.Slot == StreamMoveCursorSlot);
        Debug.Assert(clearLineInfo.Slot == StreamClearLineSlot);
        Debug.Assert(clearScreenDownInfo.Slot == StreamClearScreenDownSlot);
        Debug.Assert(fdInfo.Slot == StreamFdSlot);
        Debug.Assert(isTtyInfo.Slot == StreamIsTtySlot);
        Debug.Assert(columnsInfo.Slot == StreamColumnsSlot);
        Debug.Assert(rowsInfo.Slot == StreamRowsSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateInputShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomRead, JsShapePropertyFlags.Open, out var readInfo);
        shape = shape.GetOrAddTransition(atomSetEncoding, JsShapePropertyFlags.Open, out var setEncodingInfo);
        shape = shape.GetOrAddTransition(atomSetRawMode, JsShapePropertyFlags.Open, out var setRawModeInfo);
        shape = shape.GetOrAddTransition(atomRef, JsShapePropertyFlags.Open, out var refInfo);
        shape = shape.GetOrAddTransition(atomUnref, JsShapePropertyFlags.Open, out var unrefInfo);
        shape = shape.GetOrAddTransition(atomFd, JsShapePropertyFlags.Open, out var fdInfo);
        shape = shape.GetOrAddTransition(atomIsTty, JsShapePropertyFlags.Open, out var isTtyInfo);
        Debug.Assert(readInfo.Slot == InputReadSlot);
        Debug.Assert(setEncodingInfo.Slot == InputSetEncodingSlot);
        Debug.Assert(setRawModeInfo.Slot == InputSetRawModeSlot);
        Debug.Assert(refInfo.Slot == InputRefSlot);
        Debug.Assert(unrefInfo.Slot == InputUnrefSlot);
        Debug.Assert(fdInfo.Slot == InputFdSlot);
        Debug.Assert(isTtyInfo.Slot == InputIsTtySlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomIsAtty = EnsureAtom(realm, atomIsAtty, "isatty");
        atomWrite = EnsureAtom(realm, atomWrite, "write");
        atomCursorTo = EnsureAtom(realm, atomCursorTo, "cursorTo");
        atomMoveCursor = EnsureAtom(realm, atomMoveCursor, "moveCursor");
        atomClearLine = EnsureAtom(realm, atomClearLine, "clearLine");
        atomClearScreenDown = EnsureAtom(realm, atomClearScreenDown, "clearScreenDown");
        atomFd = EnsureAtom(realm, atomFd, "fd");
        atomIsTty = EnsureAtom(realm, atomIsTty, "isTTY");
        atomColumns = EnsureAtom(realm, atomColumns, "columns");
        atomRows = EnsureAtom(realm, atomRows, "rows");
        atomRead = EnsureAtom(realm, atomRead, "read");
        atomSetEncoding = EnsureAtom(realm, atomSetEncoding, "setEncoding");
        atomSetRawMode = EnsureAtom(realm, atomSetRawMode, "setRawMode");
        atomRef = EnsureAtom(realm, atomRef, "ref");
        atomUnref = EnsureAtom(realm, atomUnref, "unref");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static JsHostFunction CreateIsAttyFunction(JsRealm realm)
    {
        return new(realm, "isatty", 1, static (in info) =>
        {
            var fd = info.Arguments.Length == 0 ? -1 : (int)info.Realm.ToIntegerOrInfinity(info.Arguments[0]);
            var isTty = false;

            if (info.Realm.GlobalObject.TryGetProperty("process", out var processValue) &&
                processValue.TryGetObject(out var processObject))
            {
                if (fd == 1 && processObject.TryGetProperty("stdout", out var stdoutValue) &&
                    stdoutValue.TryGetObject(out var stdoutObj) &&
                    stdoutObj is JsUserDataObject<StreamState> stdoutStateObj &&
                    stdoutStateObj.UserData is not null)
                    isTty = stdoutStateObj.UserData.IsTty;
                else if (fd == 2 && processObject.TryGetProperty("stderr", out var stderrValue) &&
                         stderrValue.TryGetObject(out var stderrObj) &&
                         stderrObj is JsUserDataObject<StreamState> stderrStateObj &&
                         stderrStateObj.UserData is not null)
                    isTty = stderrStateObj.UserData.IsTty;
            }

            return isTty ? JsValue.True : JsValue.False;
        }, false);
    }

    private static JsHostFunction CreateWriteFunction(JsRealm realm)
    {
        return new(realm, "write", 1, static (in info) =>
        {
            var text = info.Arguments.Length == 0
                ? string.Empty
                : info.GetArgument(0).IsString
                    ? info.GetArgument(0).AsString()
                    : info.Realm.ToJsStringSlowPath(info.GetArgument(0));

            WriteToStream(info.ThisValue, text);
            return JsValue.True;
        }, false);
    }

    private static JsHostFunction CreateCursorToFunction(JsRealm realm)
    {
        return new(realm, "cursorTo", 2, static (in info) =>
        {
            var x = info.Arguments.Length == 0 ? 0 : (int)info.Realm.ToIntegerOrInfinity(info.Arguments[0]);
            int? y = info.Arguments.Length >= 2 ? (int)info.Realm.ToIntegerOrInfinity(info.Arguments[1]) : null;
            WriteToStream(info.ThisValue, y.HasValue ? $"\u001b[{y.Value + 1};{x + 1}H" : $"\u001b[{x + 1}G");
            return JsValue.True;
        }, false);
    }

    private static JsHostFunction CreateMoveCursorFunction(JsRealm realm)
    {
        return new(realm, "moveCursor", 2, static (in info) =>
        {
            var dx = info.Arguments.Length == 0 ? 0 : (int)info.Realm.ToIntegerOrInfinity(info.Arguments[0]);
            var dy = info.Arguments.Length < 2 ? 0 : (int)info.Realm.ToIntegerOrInfinity(info.Arguments[1]);
            if (dx < 0)
                WriteToStream(info.ThisValue, $"\u001b[{Math.Abs(dx)}D");
            else if (dx > 0)
                WriteToStream(info.ThisValue, $"\u001b[{dx}C");

            if (dy < 0)
                WriteToStream(info.ThisValue, $"\u001b[{Math.Abs(dy)}A");
            else if (dy > 0)
                WriteToStream(info.ThisValue, $"\u001b[{dy}B");

            return JsValue.True;
        }, false);
    }

    private static JsHostFunction CreateClearLineFunction(JsRealm realm)
    {
        return new(realm, "clearLine", 1, static (in info) =>
        {
            var dir = info.Arguments.Length == 0 ? 0 : (int)info.Realm.ToIntegerOrInfinity(info.Arguments[0]);
            var suffix = dir switch
            {
                -1 => "1",
                1 => "0",
                _ => "2"
            };
            WriteToStream(info.ThisValue, $"\u001b[{suffix}K");
            return JsValue.True;
        }, false);
    }

    private static JsHostFunction CreateClearScreenDownFunction(JsRealm realm)
    {
        return new(realm, "clearScreenDown", 0, static (in info) =>
        {
            WriteToStream(info.ThisValue, "\u001b[0J");
            return JsValue.True;
        }, false);
    }

    private static JsHostFunction CreateReadFunction(JsRealm realm)
    {
        return new(realm, "read", 0, static (in info) =>
        {
            _ = RequireInputObject(info.ThisValue);
            return JsValue.Null;
        }, false);
    }

    private static JsHostFunction CreateSetEncodingFunction(JsRealm realm)
    {
        return new(realm, "setEncoding", 1, static (in info) =>
        {
            var input = RequireInputObject(info.ThisValue);
            input.UserData!.Encoding = info.Arguments.Length == 0
                ? "utf8"
                : info.GetArgument(0).IsString
                    ? info.GetArgument(0).AsString()
                    : info.Realm.ToJsStringSlowPath(info.GetArgument(0));
            return info.ThisValue;
        }, false);
    }

    private static JsHostFunction CreateSetRawModeFunction(JsRealm realm)
    {
        return new(realm, "setRawMode", 1, static (in info) =>
        {
            var input = RequireInputObject(info.ThisValue);
            input.UserData!.RawModeEnabled = info.Arguments.Length != 0 && JsRealm.ToBoolean(info.GetArgument(0));
            return info.ThisValue;
        }, false);
    }

    private static JsHostFunction CreateRefFunction(JsRealm realm)
    {
        return new(realm, "ref", 0, static (in info) =>
        {
            _ = RequireInputObject(info.ThisValue);
            return info.ThisValue;
        }, false);
    }

    private static JsHostFunction CreateUnrefFunction(JsRealm realm)
    {
        return new(realm, "unref", 0, static (in info) =>
        {
            _ = RequireInputObject(info.ThisValue);
            return info.ThisValue;
        }, false);
    }

    private static void WriteToStream(JsValue thisValue, string text)
    {
        if (!thisValue.TryGetObject(out var thisObj) ||
            thisObj is not JsUserDataObject<StreamState> streamObject ||
            streamObject.UserData is null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "stream method called on incompatible receiver");

        streamObject.UserData.Writer.Write(text);
        streamObject.UserData.Writer.Flush();
    }

    private static JsUserDataObject<InputState> RequireInputObject(JsValue thisValue)
    {
        if (!thisValue.TryGetObject(out var thisObj) ||
            thisObj is not JsUserDataObject<InputState> inputObject ||
            inputObject.UserData is null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "stdin method called on incompatible receiver");

        return inputObject;
    }

    private sealed class InputState(int fd, bool isTty)
    {
        public int FileDescriptor { get; } = fd;
        public bool IsTty { get; } = isTty;
        public bool RawModeEnabled { get; set; }
        public string Encoding { get; set; } = "utf8";
    }

    private sealed class StreamState(TextWriter writer, int fd, bool isTty, int? columns, int? rows)
    {
        public TextWriter Writer { get; } = writer;
        public int FileDescriptor { get; } = fd;
        public bool IsTty { get; } = isTty;
        public int? Columns { get; } = columns;
        public int? Rows { get; } = rows;
    }
}
