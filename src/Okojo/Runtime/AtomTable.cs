using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public sealed class AtomTable
{
    public const int IdEmpty = 0;
    public const int IdConstructor = 1;
    public const int IdLength = 2;
    public const int IdName = 3;
    public const int IdPrototype = 4;
    public const int IdToString = 5;
    public const int IdCreate = 6;
    public const int IdGetPrototypeOf = 7;
    public const int IdSetPrototypeOf = 8;
    public const int IdGetOwnPropertyDescriptor = 9;
    public const int IdGetOwnPropertyNames = 10;
    public const int IdGet = 11;
    public const int IdSet = 12;
    public const int IdValue = 13;
    public const int IdWritable = 14;
    public const int IdEnumerable = 15;
    public const int IdConfigurable = 16;
    public const int IdHasOwnProperty = 17;
    public const int IdGlobalThis = 18;
    public const int IdIterator = 19;
    public const int IdSymbol = 20;
    public const int IdHasInstance = 21;
    public const int IdValueOf = 22;
    public const int IdMessage = 23;
    public const int IdStack = 24;
    public const int IdError = 25;
    public const int IdThen = 26;
    public const int IdCatch = 27;
    public const int IdResolve = 28;
    public const int IdReject = 29;
    public const int IdToStringTag = 30;
    public const int IdOkojoMeta = 31; // "__js_meta"
    public const int IdAbs = 32; // "abs"
    public const int IdAcos = 33; // "acos"
    public const int IdAcosh = 34; // "acosh"
    public const int IdAdd = 35; // "add"
    public const int IdApply = 36; // "apply"
    public const int IdAsin = 37; // "asin"
    public const int IdAsinh = 38; // "asinh"
    public const int IdAsIntN = 39; // "asIntN"
    public const int IdAssign = 40; // "assign"
    public const int IdAsUintN = 41; // "asUintN"
    public const int IdAsyncIterator = 42; // "asyncIterator"
    public const int IdAt = 43; // "at"
    public const int IdAtan = 44; // "atan"
    public const int IdAtan2 = 45; // "atan2"
    public const int IdAtanh = 46; // "atanh"
    public const int IdBind = 47; // "bind"
    public const int IdBuffer = 48; // "buffer"
    public const int IdByteLength = 49; // "byteLength"
    public const int IdByteOffset = 50; // "byteOffset"
    public const int IdBytesPerElement = 51; // "BYTES_PER_ELEMENT"
    public const int IdCall = 52; // "call"
    public const int IdCause = 53; // "cause"
    public const int IdCbrt = 54; // "cbrt"
    public const int IdCeil = 55; // "ceil"
    public const int IdClear = 56; // "clear"
    public const int IdClz32 = 57; // "clz32"
    public const int IdConcat = 58; // "concat"
    public const int IdConstruct = 59; // "construct"
    public const int IdCopyWithin = 60; // "copyWithin"
    public const int IdCos = 61; // "cos"
    public const int IdCosh = 62; // "cosh"
    public const int IdData = 63; // "data"
    public const int IdDefineProperties = 64; // "defineProperties"
    public const int IdDefineProperty = 65; // "defineProperty"
    public const int IdDelete = 66; // "delete"
    public const int IdDeleteProperty = 67; // "deleteProperty"
    public const int IdDeref = 68; // "deref"
    public const int IdDifference = 69; // "difference"
    public const int IdDone = 70; // "done"
    public const int IdDotAll = 71; // "dotAll"
    public const int IdE = 72; // "E"
    public const int IdEntries = 73; // "entries"
    public const int IdErrors = 74; // "errors"
    public const int IdEval = 75; // "eval"
    public const int IdEvery = 76; // "every"
    public const int IdExec = 77; // "exec"
    public const int IdExp = 78; // "exp"
    public const int IdExpm1 = 79; // "expm1"
    public const int IdF16Round = 80; // "f16round"
    public const int IdFill = 81; // "fill"
    public const int IdFilter = 82; // "filter"
    public const int IdFind = 83; // "find"
    public const int IdFindIndex = 84; // "findIndex"
    public const int IdFindLast = 85; // "findLast"
    public const int IdFindLastIndex = 86; // "findLastIndex"
    public const int IdFlags = 87; // "flags"
    public const int IdFlat = 88; // "flat"
    public const int IdFlatMap = 89; // "flatMap"
    public const int IdFloor = 90; // "floor"
    public const int IdFor = 91; // "for"
    public const int IdForEach = 92; // "forEach"
    public const int IdFreeze = 93; // "freeze"
    public const int IdFrom = 94; // "from"
    public const int IdFromBase64 = 95; // "fromBase64"
    public const int IdFromCharCode = 96; // "fromCharCode"
    public const int IdFromEntries = 97; // "fromEntries"
    public const int IdFromHex = 98; // "fromHex"
    public const int IdFround = 99; // "fround"
    public const int IdGetOrInsert = 100; // "getOrInsert"
    public const int IdGetOrInsertComputed = 101; // "getOrInsertComputed"
    public const int IdGetOwnPropertyDescriptors = 102; // "getOwnPropertyDescriptors"
    public const int IdGetOwnPropertySymbols = 103; // "getOwnPropertySymbols"
    public const int IdGlobal = 104; // "global"
    public const int IdGroupBy = 105; // "groupBy"
    public const int IdHas = 106; // "has"
    public const int IdHasOwn = 107; // "hasOwn"
    public const int IdHypot = 108; // "hypot"
    public const int IdIgnoreCase = 109; // "ignoreCase"
    public const int IdImul = 110; // "imul"
    public const int IdIncludes = 111; // "includes"
    public const int IdIndexOf = 112; // "indexOf"
    public const int IdInfinity = 113; // "Infinity"
    public const int IdIntersection = 114; // "intersection"
    public const int IdIs = 115; // "is"
    public const int IdIsArray = 116; // "isArray"
    public const int IdIsConcatSpreadable = 117; // "isConcatSpreadable"
    public const int IdIsDisjointFrom = 118; // "isDisjointFrom"
    public const int IdIsExtensible = 119; // "isExtensible"
    public const int IdIsFrozen = 120; // "isFrozen"
    public const int IdIsPrototypeOf = 121; // "isPrototypeOf"
    public const int IdIsSealed = 122; // "isSealed"
    public const int IdIsSubsetOf = 123; // "isSubsetOf"
    public const int IdIsSupersetOf = 124; // "isSupersetOf"
    public const int IdIsView = 125; // "isView"
    public const int IdJoin = 126; // "join"
    public const int IdKeyFor = 127; // "keyFor"
    public const int IdKeys = 128; // "keys"
    public const int IdLastIndex = 129; // "lastIndex"
    public const int IdLastIndexOf = 130; // "lastIndexOf"
    public const int IdLn10 = 131; // "LN10"
    public const int IdLn2 = 132; // "LN2"
    public const int IdLoadModule = 133; // "loadModule"
    public const int IdLog = 134; // "log"
    public const int IdLog10 = 135; // "log10"
    public const int IdLog10E = 136; // "LOG10E"
    public const int IdLog1P = 137; // "log1p"
    public const int IdLog2 = 138; // "log2"
    public const int IdLog2E = 139; // "LOG2E"
    public const int IdMap = 140; // "map"
    public const int IdMatch = 141; // "match"
    public const int IdMax = 142; // "max"
    public const int IdMaxSafeInteger = 143; // "MAX_SAFE_INTEGER"
    public const int IdMaxValue = 144; // "MAX_VALUE"
    public const int IdMaxByteLength = 145; // "maxByteLength"
    public const int IdMin = 146; // "min"
    public const int IdMinSafeInteger = 147; // "MIN_SAFE_INTEGER"
    public const int IdMinValue = 148; // "MIN_VALUE"
    public const int IdMultiline = 149; // "multiline"
    public const int IdNaN = 150; // "NaN"
    public const int IdNegativeInfinity = 151; // "NEGATIVE_INFINITY"
    public const int IdNext = 152; // "next"
    public const int IdOf = 153; // "of"
    public const int IdOnmessage = 154; // "onmessage"
    public const int IdOnmessageerror = 155; // "onmessageerror"
    public const int IdOwnKeys = 156; // "ownKeys"
    public const int IdParse = 157; // "parse"
    public const int IdPi = 158; // "PI"
    public const int IdPop = 159; // "pop"
    public const int IdPositiveInfinity = 160; // "POSITIVE_INFINITY"
    public const int IdPostMessage = 161; // "postMessage"
    public const int IdPow = 162; // "pow"
    public const int IdPreventExtensions = 163; // "preventExtensions"
    public const int IdPropertyIsEnumerable = 164; // "propertyIsEnumerable"
    public const int IdProxy = 165; // "proxy"
    public const int IdPump = 166; // "pump"
    public const int IdPush = 167; // "push"
    public const int IdRandom = 168; // "random"
    public const int IdRaw = 169; // "raw"
    public const int IdRawJson = 170; // "rawJSON"
    public const int IdRead = 171; // "read"
    public const int IdReduce = 172; // "reduce"
    public const int IdReduceRight = 173; // "reduceRight"
    public const int IdRegister = 174; // "register"
    public const int IdResizable = 175; // "resizable"
    public const int IdResize = 176; // "resize"
    public const int IdReturn = 177; // "return"
    public const int IdReverse = 178; // "reverse"
    public const int IdRevocable = 179; // "revocable"
    public const int IdRevoke = 180; // "revoke"
    public const int IdRound = 181; // "round"
    public const int IdSeal = 182; // "seal"
    public const int IdSearch = 183; // "search"
    public const int IdSetFromBase64 = 184; // "setFromBase64"
    public const int IdSetFromHex = 185; // "setFromHex"
    public const int IdShift = 186; // "shift"
    public const int IdSign = 187; // "sign"
    public const int IdSin = 188; // "sin"
    public const int IdSinh = 189; // "sinh"
    public const int IdSize = 190; // "size"
    public const int IdSlice = 191; // "slice"
    public const int IdSome = 192; // "some"
    public const int IdSort = 193; // "sort"
    public const int IdSource = 194; // "source"
    public const int IdSpecies = 195; // "species"
    public const int IdSplice = 196; // "splice"
    public const int IdSqrt = 197; // "sqrt"
    public const int IdSqrt12 = 198; // "SQRT1_2"
    public const int IdSqrt2 = 199; // "SQRT2"
    public const int IdStartsWith = 200; // "startsWith"
    public const int IdSticky = 201; // "sticky"
    public const int IdStringify = 202; // "stringify"
    public const int IdSubarray = 203; // "subarray"
    public const int IdSumPrecise = 204; // "sumPrecise"
    public const int IdSymmetricDifference = 205; // "symmetricDifference"
    public const int IdTan = 206; // "tan"
    public const int IdTanh = 207; // "tanh"
    public const int IdTerminate = 208; // "terminate"
    public const int IdTest = 209; // "test"
    public const int IdThrow = 210; // "throw"
    public const int IdToBase64 = 211; // "toBase64"
    public const int IdToHex = 212; // "toHex"
    public const int IdToJson = 213; // "toJSON"
    public const int IdToLocaleString = 214; // "toLocaleString"
    public const int IdToFixed = 215; // "toFixed"
    public const int IdToExponential = 216; // "toExponential"
    public const int IdToPrecision = 217; // "toPrecision"
    public const int IdToPrimitive = 218; // "toPrimitive"
    public const int IdToReversed = 219; // "toReversed"
    public const int IdToSorted = 220; // "toSorted"
    public const int IdToSpliced = 221; // "toSpliced"
    public const int IdToArray = 222; // "toArray"
    public const int IdTrunc = 223; // "trunc"
    public const int IdUndefined = 224; // "undefined"
    public const int IdUnicode = 225; // "unicode"
    public const int IdUnion = 226; // "union"
    public const int IdUnregister = 227; // "unregister"
    public const int IdUnscopables = 228; // "unscopables"
    public const int IdUnshift = 229; // "unshift"
    public const int IdValues = 230; // "values"
    public const int IdWith = 231; // "with"
    public const int IdWritten = 232; // "written"
    public const int IdEpsilon = 233; // "EPSILON"
    public const int IdParseFloat = 234; // "parseFloat"
    public const int IdParseInt = 235; // "parseInt"
    public const int IdIsFinite = 236; // "isFinite"
    public const int IdIsInteger = 237; // "isInteger"
    public const int IdIsNaN = 238; // "isNaN"
    public const int IdIsSafeInteger = 239; // "isSafeInteger"
    public const int IdFromAsync = 240; // "fromAsync"
    public const int IdDescription = 241; // "description"
    public const int IdDetached = 242; // "detached"
    public const int IdTransfer = 243; // "transfer"
    public const int IdTransferToFixedLength = 244; // "transferToFixedLength"
    public const int IdTransferToImmutable = 245; // "transferToImmutable"
    public const int IdSliceToImmutable = 246; // "sliceToImmutable"
    public const int IdDrop = 247; // "drop"
    public const int IdTake = 248; // "take"
    public const int IdDispose = 249; // "dispose"
    public const int IdCharAt = 250; // "charAt"
    public const int IdCharCodeAt = 251; // "charCodeAt"
    public const int IdCodePointAt = 252; // "codePointAt"
    public const int IdEndsWith = 253; // "endsWith"
    public const int IdFromCodePoint = 254; // "fromCodePoint"
    public const int IdLocaleCompare = 255; // "localeCompare"
    public const int IdSubstring = 256; // "substring"
    public const int IdToLowerCase = 257; // "toLowerCase"
    public const int IdToUpperCase = 258; // "toUpperCase"
    public const int IdTrim = 259; // "trim"
    public const int IdTrimEnd = 260; // "trimEnd"
    public const int IdTrimStart = 261; // "trimStart"
    public const int IdIsWellFormed = 262; // "isWellFormed"
    public const int IdToWellFormed = 263; // "toWellFormed"
    public const int IdPadEnd = 264; // "padEnd"
    public const int IdPadStart = 265; // "padStart"
    public const int IdRepeat = 266; // "repeat"
    public const int IdReplace = 267; // "replace"
    public const int IdReplaceAll = 268; // "replaceAll"
    public const int IdToLocaleLowerCase = 269; // "toLocaleLowerCase"
    public const int IdToLocaleUpperCase = 270; // "toLocaleUpperCase"
    public const int IdSplit = 271; // "split"
    public const int IdNormalize = 272; // "normalize"
    public const int IdMatchAll = 273; // "matchAll"
    public const int IdNow = 274; // "now"
    public const int IdUTC = 275; // "UTC"
    public const int IdGetDate = 276; // "getDate"
    public const int IdGetDay = 277; // "getDay"
    public const int IdGetFullYear = 278; // "getFullYear"
    public const int IdGetHours = 279; // "getHours"
    public const int IdGetMilliseconds = 280; // "getMilliseconds"
    public const int IdGetMinutes = 281; // "getMinutes"
    public const int IdGetMonth = 282; // "getMonth"
    public const int IdGetSeconds = 283; // "getSeconds"
    public const int IdGetTime = 284; // "getTime"
    public const int IdGetTimezoneOffset = 285; // "getTimezoneOffset"
    public const int IdGetUtcDate = 286; // "getUTCDate"
    public const int IdGetUtcDay = 287; // "getUTCDay"
    public const int IdGetUtcFullYear = 288; // "getUTCFullYear"
    public const int IdGetUtcHours = 289; // "getUTCHours"
    public const int IdGetUtcMilliseconds = 290; // "getUTCMilliseconds"
    public const int IdGetUtcMinutes = 291; // "getUTCMinutes"
    public const int IdGetUtcMonth = 292; // "getUTCMonth"
    public const int IdGetUtcSeconds = 293; // "getUTCSeconds"
    public const int IdSetDate = 294; // "setDate"
    public const int IdSetFullYear = 295; // "setFullYear"
    public const int IdSetHours = 296; // "setHours"
    public const int IdSetMilliseconds = 297; // "setMilliseconds"
    public const int IdSetMinutes = 298; // "setMinutes"
    public const int IdSetMonth = 299; // "setMonth"
    public const int IdSetSeconds = 300; // "setSeconds"
    public const int IdSetTime = 301; // "setTime"
    public const int IdSetUtcDate = 302; // "setUTCDate"
    public const int IdSetUtcFullYear = 303; // "setUTCFullYear"
    public const int IdSetUtcHours = 304; // "setUTCHours"
    public const int IdSetUtcMilliseconds = 305; // "setUTCMilliseconds"
    public const int IdSetUtcMinutes = 306; // "setUTCMinutes"
    public const int IdSetUtcMonth = 307; // "setUTCMonth"
    public const int IdSetUtcSeconds = 308; // "setUTCSeconds"
    public const int IdToLocaleDateString = 309; // "toLocaleDateString"
    public const int IdToLocaleTimeString = 310; // "toLocaleTimeString"
    public const int IdToDateString = 311; // "toDateString"
    public const int IdToIsoString = 312; // "toISOString"
    public const int IdToTimeString = 313; // "toTimeString"
    public const int IdToUtcString = 314; // "toUTCString"
    public const int IdSharedArrayBuffer = 315; // "SharedArrayBuffer"
    public const int IdAtomics = 316; // "Atomics"
    public const int IdAnd = 317; // "and"
    public const int IdCompareExchange = 318; // "compareExchange"
    public const int IdExchange = 319; // "exchange"
    public const int IdGrow = 320; // "grow"
    public const int IdGrowable = 321; // "growable"
    public const int IdIsLockFree = 322; // "isLockFree"
    public const int IdLoad = 323; // "load"
    public const int IdNotify = 324; // "notify"
    public const int IdOr = 325; // "or"
    public const int IdPause = 326; // "pause"
    public const int IdStore = 327; // "store"
    public const int IdSub = 328; // "sub"
    public const int IdWait = 329; // "wait"
    public const int IdWaitAsync = 330; // "waitAsync"
    public const int IdAsync = 331; // "async"
    public const int IdXor = 332; // "xor"
    public const int IdGetCanonicalLocales = 333; // "getCanonicalLocales"
    public const int IdSupportedValuesOf = 334; // "supportedValuesOf"
    public const int IdLocale = 335; // "Locale"
    public const int IdSegmenter = 336; // "Segmenter"
    public const int IdRelativeTimeFormat = 337; // "RelativeTimeFormat"
    public const int IdDurationFormat = 338; // "DurationFormat"
    public const int IdDisplayNames = 339; // "DisplayNames"
    public const int IdListFormat = 340; // "ListFormat"
    public const int IdCollator = 341; // "Collator"
    public const int IdDateTimeFormat = 342; // "DateTimeFormat"
    public const int IdNumberFormat = 343; // "NumberFormat"
    public const int IdPluralRules = 344; // "PluralRules"
    public const int IdSupportedLocalesOf = 345; // "supportedLocalesOf"
    public const int IdMaximize = 346; // "maximize"
    public const int IdMinimize = 347; // "minimize"
    public const int IdGetCalendars = 348; // "getCalendars"
    public const int IdGetCollations = 349; // "getCollations"
    public const int IdGetHourCycles = 350; // "getHourCycles"
    public const int IdGetNumberingSystems = 351; // "getNumberingSystems"
    public const int IdGetTimeZones = 352; // "getTimeZones"
    public const int IdGetTextInfo = 353; // "getTextInfo"
    public const int IdGetWeekInfo = 354; // "getWeekInfo"
    public const int IdBaseName = 355; // "baseName"
    public const int IdCalendar = 356; // "calendar"
    public const int IdCaseFirst = 357; // "caseFirst"
    public const int IdCollation = 358; // "collation"
    public const int IdHourCycle = 359; // "hourCycle"
    public const int IdLanguage = 360; // "language"
    public const int IdNumberingSystem = 361; // "numberingSystem"
    public const int IdNumeric = 362; // "numeric"
    public const int IdRegion = 363; // "region"
    public const int IdScript = 364; // "script"
    public const int IdFirstDayOfWeek = 365; // "firstDayOfWeek"
    public const int IdVariants = 366; // "variants"
    public const int IdLocaleLower = 367; // "locale"
    public const int IdGranularity = 368; // "granularity"
    public const int IdSegment = 369; // "segment"
    public const int IdResolvedOptions = 370; // "resolvedOptions"
    public const int IdContaining = 371; // "containing"
    public const int IdStyle = 372; // "style"
    public const int IdYears = 373; // "years"
    public const int IdYearsDisplay = 374; // "yearsDisplay"
    public const int IdMonths = 375; // "months"
    public const int IdMonthsDisplay = 376; // "monthsDisplay"
    public const int IdWeeks = 377; // "weeks"
    public const int IdWeeksDisplay = 378; // "weeksDisplay"
    public const int IdDays = 379; // "days"
    public const int IdDaysDisplay = 380; // "daysDisplay"
    public const int IdHours = 381; // "hours"
    public const int IdHoursDisplay = 382; // "hoursDisplay"
    public const int IdMinutes = 383; // "minutes"
    public const int IdMinutesDisplay = 384; // "minutesDisplay"
    public const int IdSeconds = 385; // "seconds"
    public const int IdSecondsDisplay = 386; // "secondsDisplay"
    public const int IdMilliseconds = 387; // "milliseconds"
    public const int IdMillisecondsDisplay = 388; // "millisecondsDisplay"
    public const int IdMicroseconds = 389; // "microseconds"
    public const int IdMicrosecondsDisplay = 390; // "microsecondsDisplay"
    public const int IdNanoseconds = 391; // "nanoseconds"
    public const int IdNanosecondsDisplay = 392; // "nanosecondsDisplay"
    public const int IdFractionalDigits = 393; // "fractionalDigits"
    public const int IdFormat = 394; // "format"
    public const int IdFormatToParts = 395; // "formatToParts"
    public const int IdCurrency = 396; // "currency"
    public const int IdCurrencyDisplay = 397; // "currencyDisplay"
    public const int IdCurrencySign = 398; // "currencySign"
    public const int IdUnit = 399; // "unit"
    public const int IdUnitDisplay = 400; // "unitDisplay"
    public const int IdMinimumIntegerDigits = 401; // "minimumIntegerDigits"
    public const int IdMinimumFractionDigits = 402; // "minimumFractionDigits"
    public const int IdMaximumFractionDigits = 403; // "maximumFractionDigits"
    public const int IdMinimumSignificantDigits = 404; // "minimumSignificantDigits"
    public const int IdMaximumSignificantDigits = 405; // "maximumSignificantDigits"
    public const int IdUseGrouping = 406; // "useGrouping"
    public const int IdNotation = 407; // "notation"
    public const int IdCompactDisplay = 408; // "compactDisplay"
    public const int IdSignDisplay = 409; // "signDisplay"
    public const int IdRoundingIncrement = 410; // "roundingIncrement"
    public const int IdRoundingMode = 411; // "roundingMode"
    public const int IdRoundingPriority = 412; // "roundingPriority"
    public const int IdTrailingZeroDisplay = 413; // "trailingZeroDisplay"
    public const int IdFormatRange = 414; // "formatRange"
    public const int IdFormatRangeToParts = 415; // "formatRangeToParts"
    public const int IdTimeZone = 416; // "timeZone"
    public const int IdHour12 = 417; // "hour12"
    public const int IdFractionalSecondDigits = 418; // "fractionalSecondDigits"
    public const int IdType = 419; // "type"
    public const int IdFallback = 420; // "fallback"
    public const int IdLanguageDisplay = 421; // "languageDisplay"
    public const int IdUsage = 422; // "usage"
    public const int IdSensitivity = 423; // "sensitivity"
    public const int IdIgnorePunctuation = 424; // "ignorePunctuation"
    public const int IdCompare = 425; // "compare"
    public const int IdPluralCategories = 426; // "pluralCategories"
    public const int IdSelect = 427; // "select"
    public const int IdSelectRange = 428; // "selectRange"
    public const int IdFirstDay = 429; // "firstDay"
    public const int IdWeekend = 430; // "weekend"
    public const int IdDirection = 431; // "direction"
    public const int IdWeekday = 432; // "weekday"
    public const int IdEra = 433; // "era"
    public const int IdYear = 434; // "year"
    public const int IdMonth = 435; // "month"
    public const int IdDay = 436; // "day"
    public const int IdDayPeriod = 437; // "dayPeriod"
    public const int IdHour = 438; // "hour"
    public const int IdMinute = 439; // "minute"
    public const int IdSecond = 440; // "second"
    public const int IdTimeZoneName = 441; // "timeZoneName"
    public const int IdDateStyle = 442; // "dateStyle"
    public const int IdTimeStyle = 443; // "timeStyle"
    public const int IdFinally = 444; // "finally"
    public const int IdTry = 445; // "try"
    public const int IdRace = 446; // "race"
    public const int IdAll = 447; // "all"
    public const int IdAny = 448; // "any"
    public const int IdAllSettled = 449; // "allSettled"
    public const int IdWithResolvers = 450; // "withResolvers"
    public const int IdGroups = 451; // "groups"
    public const int IdIndex = 452; // "index"
    public const int IdAsyncDispose = 453; // "asyncDispose"
    public const int IdAdopt = 454; // "adopt"
    public const int IdDefer = 455; // "defer"
    public const int IdDisposed = 456; // "disposed"
    public const int IdErrorProperty = 457; // "error"
    public const int IdMove = 458; // "move"
    public const int IdPromise = 459; // "promise"
    public const int IdSuppressed = 460; // "suppressed"
    public const int IdUse = 461; // "use"
    public const int IdDisposeAsync = 462; // "disposeAsync"


    // Symbol-keyed property atoms are negative to avoid collisions with string atoms.
    public const int IdSymbolIterator = -1;
    public const int IdSymbolHasInstance = -2;
    public const int IdSymbolToStringTag = -3;
    public const int IdSymbolToPrimitive = -4;
    public const int IdSymbolSpecies = -5;
    public const int IdSymbolAsyncIterator = -6;
    public const int IdSymbolIsConcatSpreadable = -7;
    public const int IdSymbolMatch = -8;
    public const int IdSymbolUnscopables = -9;
    public const int IdSymbolDispose = -10;
    public const int IdSymbolReplace = -11;
    public const int IdSymbolMatchAll = -12;
    public const int IdSymbolSplit = -13;
    public const int IdSymbolSearch = -14;
    public const int IdSymbolAsyncDispose = -15;

    private static readonly string[] PredefinedAtoms =
    {
        string.Empty,
        "constructor",
        "length",
        "name",
        "prototype",
        "toString",
        "create",
        "getPrototypeOf",
        "setPrototypeOf",
        "getOwnPropertyDescriptor",
        "getOwnPropertyNames",
        "get",
        "set",
        "value",
        "writable",
        "enumerable",
        "configurable",
        "hasOwnProperty",
        "globalThis",
        "iterator",
        "Symbol",
        "hasInstance",
        "valueOf",
        "message",
        "stack",
        "Error",
        "then",
        "catch",
        "resolve",
        "reject",
        "toStringTag",
        "__js_meta",
        "abs",
        "acos",
        "acosh",
        "add",
        "apply",
        "asin",
        "asinh",
        "asIntN",
        "assign",
        "asUintN",
        "asyncIterator",
        "at",
        "atan",
        "atan2",
        "atanh",
        "bind",
        "buffer",
        "byteLength",
        "byteOffset",
        "BYTES_PER_ELEMENT",
        "call",
        "cause",
        "cbrt",
        "ceil",
        "clear",
        "clz32",
        "concat",
        "construct",
        "copyWithin",
        "cos",
        "cosh",
        "data",
        "defineProperties",
        "defineProperty",
        "delete",
        "deleteProperty",
        "deref",
        "difference",
        "done",
        "dotAll",
        "E",
        "entries",
        "errors",
        "eval",
        "every",
        "exec",
        "exp",
        "expm1",
        "f16round",
        "fill",
        "filter",
        "find",
        "findIndex",
        "findLast",
        "findLastIndex",
        "flags",
        "flat",
        "flatMap",
        "floor",
        "for",
        "forEach",
        "freeze",
        "from",
        "fromBase64",
        "fromCharCode",
        "fromEntries",
        "fromHex",
        "fround",
        "getOrInsert",
        "getOrInsertComputed",
        "getOwnPropertyDescriptors",
        "getOwnPropertySymbols",
        "global",
        "groupBy",
        "has",
        "hasOwn",
        "hypot",
        "ignoreCase",
        "imul",
        "includes",
        "indexOf",
        "Infinity",
        "intersection",
        "is",
        "isArray",
        "isConcatSpreadable",
        "isDisjointFrom",
        "isExtensible",
        "isFrozen",
        "isPrototypeOf",
        "isSealed",
        "isSubsetOf",
        "isSupersetOf",
        "isView",
        "join",
        "keyFor",
        "keys",
        "lastIndex",
        "lastIndexOf",
        "LN10",
        "LN2",
        "loadModule",
        "log",
        "log10",
        "LOG10E",
        "log1p",
        "log2",
        "LOG2E",
        "map",
        "match",
        "max",
        "MAX_SAFE_INTEGER",
        "MAX_VALUE",
        "maxByteLength",
        "min",
        "MIN_SAFE_INTEGER",
        "MIN_VALUE",
        "multiline",
        "NaN",
        "NEGATIVE_INFINITY",
        "next",
        "of",
        "onmessage",
        "onmessageerror",
        "ownKeys",
        "parse",
        "PI",
        "pop",
        "POSITIVE_INFINITY",
        "postMessage",
        "pow",
        "preventExtensions",
        "propertyIsEnumerable",
        "proxy",
        "pump",
        "push",
        "random",
        "raw",
        "rawJSON",
        "read",
        "reduce",
        "reduceRight",
        "register",
        "resizable",
        "resize",
        "return",
        "reverse",
        "revocable",
        "revoke",
        "round",
        "seal",
        "search",
        "setFromBase64",
        "setFromHex",
        "shift",
        "sign",
        "sin",
        "sinh",
        "size",
        "slice",
        "some",
        "sort",
        "source",
        "species",
        "splice",
        "sqrt",
        "SQRT1_2",
        "SQRT2",
        "startsWith",
        "sticky",
        "stringify",
        "subarray",
        "sumPrecise",
        "symmetricDifference",
        "tan",
        "tanh",
        "terminate",
        "test",
        "throw",
        "toBase64",
        "toHex",
        "toJSON",
        "toLocaleString",
        "toFixed",
        "toExponential",
        "toPrecision",
        "toPrimitive",
        "toReversed",
        "toSorted",
        "toSpliced",
        "toArray",
        "trunc",
        "undefined",
        "unicode",
        "union",
        "unregister",
        "unscopables",
        "unshift",
        "values",
        "with",
        "written",
        "EPSILON",
        "parseFloat",
        "parseInt",
        "isFinite",
        "isInteger",
        "isNaN",
        "isSafeInteger",
        "fromAsync",
        "description",
        "detached",
        "transfer",
        "transferToFixedLength",
        "transferToImmutable",
        "sliceToImmutable",
        "drop",
        "take",
        "dispose",
        "charAt",
        "charCodeAt",
        "codePointAt",
        "endsWith",
        "fromCodePoint",
        "localeCompare",
        "substring",
        "toLowerCase",
        "toUpperCase",
        "trim",
        "trimEnd",
        "trimStart",
        "isWellFormed",
        "toWellFormed",
        "padEnd",
        "padStart",
        "repeat",
        "replace",
        "replaceAll",
        "toLocaleLowerCase",
        "toLocaleUpperCase",
        "split",
        "normalize",
        "matchAll",
        "now",
        "UTC",
        "getDate",
        "getDay",
        "getFullYear",
        "getHours",
        "getMilliseconds",
        "getMinutes",
        "getMonth",
        "getSeconds",
        "getTime",
        "getTimezoneOffset",
        "getUTCDate",
        "getUTCDay",
        "getUTCFullYear",
        "getUTCHours",
        "getUTCMilliseconds",
        "getUTCMinutes",
        "getUTCMonth",
        "getUTCSeconds",
        "setDate",
        "setFullYear",
        "setHours",
        "setMilliseconds",
        "setMinutes",
        "setMonth",
        "setSeconds",
        "setTime",
        "setUTCDate",
        "setUTCFullYear",
        "setUTCHours",
        "setUTCMilliseconds",
        "setUTCMinutes",
        "setUTCMonth",
        "setUTCSeconds",
        "toLocaleDateString",
        "toLocaleTimeString",
        "toDateString",
        "toISOString",
        "toTimeString",
        "toUTCString",
        "SharedArrayBuffer",
        "Atomics",
        "and",
        "compareExchange",
        "exchange",
        "grow",
        "growable",
        "isLockFree",
        "load",
        "notify",
        "or",
        "pause",
        "store",
        "sub",
        "wait",
        "waitAsync",
        "async",
        "xor",
        "getCanonicalLocales",
        "supportedValuesOf",
        "Locale",
        "Segmenter",
        "RelativeTimeFormat",
        "DurationFormat",
        "DisplayNames",
        "ListFormat",
        "Collator",
        "DateTimeFormat",
        "NumberFormat",
        "PluralRules",
        "supportedLocalesOf",
        "maximize",
        "minimize",
        "getCalendars",
        "getCollations",
        "getHourCycles",
        "getNumberingSystems",
        "getTimeZones",
        "getTextInfo",
        "getWeekInfo",
        "baseName",
        "calendar",
        "caseFirst",
        "collation",
        "hourCycle",
        "language",
        "numberingSystem",
        "numeric",
        "region",
        "script",
        "firstDayOfWeek",
        "variants",
        "locale",
        "granularity",
        "segment",
        "resolvedOptions",
        "containing",
        "style",
        "years",
        "yearsDisplay",
        "months",
        "monthsDisplay",
        "weeks",
        "weeksDisplay",
        "days",
        "daysDisplay",
        "hours",
        "hoursDisplay",
        "minutes",
        "minutesDisplay",
        "seconds",
        "secondsDisplay",
        "milliseconds",
        "millisecondsDisplay",
        "microseconds",
        "microsecondsDisplay",
        "nanoseconds",
        "nanosecondsDisplay",
        "fractionalDigits",
        "format",
        "formatToParts",
        "currency",
        "currencyDisplay",
        "currencySign",
        "unit",
        "unitDisplay",
        "minimumIntegerDigits",
        "minimumFractionDigits",
        "maximumFractionDigits",
        "minimumSignificantDigits",
        "maximumSignificantDigits",
        "useGrouping",
        "notation",
        "compactDisplay",
        "signDisplay",
        "roundingIncrement",
        "roundingMode",
        "roundingPriority",
        "trailingZeroDisplay",
        "formatRange",
        "formatRangeToParts",
        "timeZone",
        "hour12",
        "fractionalSecondDigits",
        "type",
        "fallback",
        "languageDisplay",
        "usage",
        "sensitivity",
        "ignorePunctuation",
        "compare",
        "pluralCategories",
        "select",
        "selectRange",
        "firstDay",
        "weekend",
        "direction",
        "weekday",
        "era",
        "year",
        "month",
        "day",
        "dayPeriod",
        "hour",
        "minute",
        "second",
        "timeZoneName",
        "dateStyle",
        "timeStyle",
        "finally",
        "try",
        "race",
        "all",
        "any",
        "allSettled",
        "withResolvers",
        "groups",
        "index",
        "asyncDispose",
        "adopt",
        "defer",
        "disposed",
        "error",
        "move",
        "promise",
        "suppressed",
        "use",
        "disposeAsync"
    };

    private static readonly string[] PredefinedSymbolAtoms =
    [
        "Symbol.iterator",
        "Symbol.hasInstance",
        "Symbol.toStringTag",
        "Symbol.toPrimitive",
        "Symbol.species",
        "Symbol.asyncIterator",
        "Symbol.isConcatSpreadable",
        "Symbol.match",
        "Symbol.unscopables",
        "Symbol.dispose",
        "Symbol.replace",
        "Symbol.matchAll",
        "Symbol.split",
        "Symbol.search",
        "Symbol.asyncDispose"
    ];

    private readonly Dictionary<string, int> atomByString = new(StringComparer.Ordinal);
    private readonly List<string> stringByAtom = new();
    private readonly List<Symbol> symbolByAtom = new();


    public AtomTable()
    {
        var predefinedSymbolSet = new HashSet<string>(StringComparer.Ordinal);
        stringByAtom.Capacity = PredefinedAtoms.Length;
        for (var i = 0; i < PredefinedAtoms.Length; i++)
        {
            var name = PredefinedAtoms[i];
            stringByAtom.Add(name);
            atomByString[name] = i;
        }

        symbolByAtom.Capacity = PredefinedSymbolAtoms.Length;
        for (var i = 0; i < PredefinedSymbolAtoms.Length; i++)
        {
            var name = PredefinedSymbolAtoms[i];
            Debug.Assert(predefinedSymbolSet.Add(name), $"Duplicate predefined symbol string: {name}");
            var atom = -i - 1;
            symbolByAtom.Add(new(atom, name, true));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetArrayIndexFromCanonicalString(string text, out uint index)
    {
        index = 0;
        if (string.IsNullOrEmpty(text)) return false;

        var firstChar = text[0];
        if (firstChar is < '0' or > '9') return false;
        return TryGetArrayIndexFromCanonicalStringSlowPath(text, out index);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TryGetArrayIndexFromCanonicalStringSlowPath(string text, out uint index)
        {
            index = 0;
            if (text.Length > 1 && text[0] == '0')
            {
                index = 0;
                return false;
            }

            uint value = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if ((uint)(c - '0') > 9u)
                {
                    index = 0;
                    return false;
                }

                var digit = (uint)(c - '0');
                if (value > (uint.MaxValue - digit) / 10u)
                {
                    index = 0;
                    return false;
                }

                value = value * 10u + digit;
            }

            if (value == uint.MaxValue)
            {
                index = 0;
                return false;
            }

            index = value;
            return true;
        }
    }

    public bool TryGetInterned(string name, out int atom)
    {
        return atomByString.TryGetValue(name, out atom);
    }

    public int InternNoCheck(string name)
    {
        if (atomByString.TryGetValue(name, out var atom))
            return atom;
#if DEBUG
        if (TryGetArrayIndexFromCanonicalString(name, out _))
            throw new InvalidOperationException(
                $"InternNoCheck received canonical array-index string '{name}'.");
#endif

        atom = stringByAtom.Count;
        atomByString.Add(name, atom);
        stringByAtom.Add(name);
        return atom;
    }

    public int InternSymbolString(string? description)
    {
        var atom = -symbolByAtom.Count - 1;
        symbolByAtom.Add(new(atom, description));
        return atom;
    }

    public string AtomToString(int atom)
    {
        if (atom < 0)
        {
            var symbolIndex = -atom - 1;
            if ((uint)symbolIndex < (uint)symbolByAtom.Count)
                return symbolByAtom[symbolIndex].Description ?? string.Empty;
        }
        else if ((uint)atom < (uint)stringByAtom.Count)
        {
            return stringByAtom[atom];
        }

        throw new KeyNotFoundException($"Unknown atom id: {atom}");
    }

    public bool TryGetSymbolByAtom(int atom, out Symbol symbol)
    {
        if (atom < 0)
        {
            var symbolIndex = -atom - 1;
            if ((uint)symbolIndex < (uint)symbolByAtom.Count)
            {
                symbol = symbolByAtom[symbolIndex];
                return true;
            }
        }

        symbol = null!;
        return false;
    }
}
