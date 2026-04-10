namespace Okojo.RegExp;

internal static class ScratchUnicodeScriptTables
{
    private static readonly (int Start, int End)[] AdlamRanges =
    [
        (0x1E900, 0x1E94B),
        (0x1E950, 0x1E959),
        (0x1E95E, 0x1E95F)
    ];

    private static readonly (int Start, int End)[] AhomRanges =
    [
        (0x11700, 0x1171A),
        (0x1171D, 0x1172B),
        (0x11730, 0x11746)
    ];

    private static readonly (int Start, int End)[] AnatolianHieroglyphsRanges =
    [
        (0x14400, 0x14646)
    ];

    private static readonly (int Start, int End)[] ArabicRanges =
    [
        (0x600, 0x604),
        (0x606, 0x60B),
        (0x60D, 0x61A),
        (0x61C, 0x61E),
        (0x620, 0x63F),
        (0x641, 0x64A),
        (0x656, 0x66F),
        (0x671, 0x6DC),
        (0x6DE, 0x6FF),
        (0x750, 0x77F),
        (0x870, 0x891),
        (0x897, 0x8E1),
        (0x8E3, 0x8FF),
        (0xFB50, 0xFD3D),
        (0xFD40, 0xFDCF),
        (0xFDF0, 0xFDFF),
        (0xFE70, 0xFE74),
        (0xFE76, 0xFEFC),
        (0x10E60, 0x10E7E),
        (0x10EC2, 0x10EC7),
        (0x10ED0, 0x10ED8),
        (0x10EFA, 0x10EFF),
        (0x1EE00, 0x1EE03),
        (0x1EE05, 0x1EE1F),
        (0x1EE21, 0x1EE22),
        (0x1EE24, 0x1EE24),
        (0x1EE27, 0x1EE27),
        (0x1EE29, 0x1EE32),
        (0x1EE34, 0x1EE37),
        (0x1EE39, 0x1EE39),
        (0x1EE3B, 0x1EE3B),
        (0x1EE42, 0x1EE42),
        (0x1EE47, 0x1EE47),
        (0x1EE49, 0x1EE49),
        (0x1EE4B, 0x1EE4B),
        (0x1EE4D, 0x1EE4F),
        (0x1EE51, 0x1EE52),
        (0x1EE54, 0x1EE54),
        (0x1EE57, 0x1EE57),
        (0x1EE59, 0x1EE59),
        (0x1EE5B, 0x1EE5B),
        (0x1EE5D, 0x1EE5D),
        (0x1EE5F, 0x1EE5F),
        (0x1EE61, 0x1EE62),
        (0x1EE64, 0x1EE64),
        (0x1EE67, 0x1EE6A),
        (0x1EE6C, 0x1EE72),
        (0x1EE74, 0x1EE77),
        (0x1EE79, 0x1EE7C),
        (0x1EE7E, 0x1EE7E),
        (0x1EE80, 0x1EE89),
        (0x1EE8B, 0x1EE9B),
        (0x1EEA1, 0x1EEA3),
        (0x1EEA5, 0x1EEA9),
        (0x1EEAB, 0x1EEBB),
        (0x1EEF0, 0x1EEF1)
    ];

    private static readonly (int Start, int End)[] ArmenianRanges =
    [
        (0x531, 0x556),
        (0x559, 0x58A),
        (0x58D, 0x58F),
        (0xFB13, 0xFB17)
    ];

    private static readonly (int Start, int End)[] AvestanRanges =
    [
        (0x10B00, 0x10B35),
        (0x10B39, 0x10B3F)
    ];

    private static readonly (int Start, int End)[] BalineseRanges =
    [
        (0x1B00, 0x1B4C),
        (0x1B4E, 0x1B7F)
    ];

    private static readonly (int Start, int End)[] BamumRanges =
    [
        (0xA6A0, 0xA6F7),
        (0x16800, 0x16A38)
    ];

    private static readonly (int Start, int End)[] BassaVahRanges =
    [
        (0x16AD0, 0x16AED),
        (0x16AF0, 0x16AF5)
    ];

    private static readonly (int Start, int End)[] BatakRanges =
    [
        (0x1BC0, 0x1BF3),
        (0x1BFC, 0x1BFF)
    ];

    private static readonly (int Start, int End)[] BengaliRanges =
    [
        (0x980, 0x983),
        (0x985, 0x98C),
        (0x98F, 0x990),
        (0x993, 0x9A8),
        (0x9AA, 0x9B0),
        (0x9B2, 0x9B2),
        (0x9B6, 0x9B9),
        (0x9BC, 0x9C4),
        (0x9C7, 0x9C8),
        (0x9CB, 0x9CE),
        (0x9D7, 0x9D7),
        (0x9DC, 0x9DD),
        (0x9DF, 0x9E3),
        (0x9E6, 0x9FE)
    ];

    private static readonly (int Start, int End)[] BeriaErfeRanges =
    [
        (0x16EA0, 0x16EB8),
        (0x16EBB, 0x16ED3)
    ];

    private static readonly (int Start, int End)[] BhaiksukiRanges =
    [
        (0x11C00, 0x11C08),
        (0x11C0A, 0x11C36),
        (0x11C38, 0x11C45),
        (0x11C50, 0x11C6C)
    ];

    private static readonly (int Start, int End)[] BopomofoRanges =
    [
        (0x2EA, 0x2EB),
        (0x3105, 0x312F),
        (0x31A0, 0x31BF)
    ];

    private static readonly (int Start, int End)[] BrahmiRanges =
    [
        (0x11000, 0x1104D),
        (0x11052, 0x11075),
        (0x1107F, 0x1107F)
    ];

    private static readonly (int Start, int End)[] BrailleRanges =
    [
        (0x2800, 0x28FF)
    ];

    private static readonly (int Start, int End)[] BugineseRanges =
    [
        (0x1A00, 0x1A1B),
        (0x1A1E, 0x1A1F)
    ];

    private static readonly (int Start, int End)[] BuhidRanges =
    [
        (0x1740, 0x1753)
    ];

    private static readonly (int Start, int End)[] CanadianAboriginalRanges =
    [
        (0x1400, 0x167F),
        (0x18B0, 0x18F5),
        (0x11AB0, 0x11ABF)
    ];

    private static readonly (int Start, int End)[] CarianRanges =
    [
        (0x102A0, 0x102D0)
    ];

    private static readonly (int Start, int End)[] CaucasianAlbanianRanges =
    [
        (0x10530, 0x10563),
        (0x1056F, 0x1056F)
    ];

    private static readonly (int Start, int End)[] ChakmaRanges =
    [
        (0x11100, 0x11134),
        (0x11136, 0x11147)
    ];

    private static readonly (int Start, int End)[] ChamRanges =
    [
        (0xAA00, 0xAA36),
        (0xAA40, 0xAA4D),
        (0xAA50, 0xAA59),
        (0xAA5C, 0xAA5F)
    ];

    private static readonly (int Start, int End)[] CherokeeRanges =
    [
        (0x13A0, 0x13F5),
        (0x13F8, 0x13FD),
        (0xAB70, 0xABBF)
    ];

    private static readonly (int Start, int End)[] ChorasmianRanges =
    [
        (0x10FB0, 0x10FCB)
    ];

    private static readonly (int Start, int End)[] CommonRanges =
    [
        (0x0, 0x40),
        (0x5B, 0x60),
        (0x7B, 0xA9),
        (0xAB, 0xB9),
        (0xBB, 0xBF),
        (0xD7, 0xD7),
        (0xF7, 0xF7),
        (0x2B9, 0x2DF),
        (0x2E5, 0x2E9),
        (0x2EC, 0x2FF),
        (0x374, 0x374),
        (0x37E, 0x37E),
        (0x385, 0x385),
        (0x387, 0x387),
        (0x605, 0x605),
        (0x60C, 0x60C),
        (0x61B, 0x61B),
        (0x61F, 0x61F),
        (0x640, 0x640),
        (0x6DD, 0x6DD),
        (0x8E2, 0x8E2),
        (0x964, 0x965),
        (0xE3F, 0xE3F),
        (0xFD5, 0xFD8),
        (0x10FB, 0x10FB),
        (0x16EB, 0x16ED),
        (0x1735, 0x1736),
        (0x1802, 0x1803),
        (0x1805, 0x1805),
        (0x1CD3, 0x1CD3),
        (0x1CE1, 0x1CE1),
        (0x1CE9, 0x1CEC),
        (0x1CEE, 0x1CF3),
        (0x1CF5, 0x1CF7),
        (0x1CFA, 0x1CFA),
        (0x2000, 0x200B),
        (0x200E, 0x2064),
        (0x2066, 0x2070),
        (0x2074, 0x207E),
        (0x2080, 0x208E),
        (0x20A0, 0x20C1),
        (0x2100, 0x2125),
        (0x2127, 0x2129),
        (0x212C, 0x2131),
        (0x2133, 0x214D),
        (0x214F, 0x215F),
        (0x2189, 0x218B),
        (0x2190, 0x2429),
        (0x2440, 0x244A),
        (0x2460, 0x27FF),
        (0x2900, 0x2B73),
        (0x2B76, 0x2BFF),
        (0x2E00, 0x2E5D),
        (0x2FF0, 0x3004),
        (0x3006, 0x3006),
        (0x3008, 0x3020),
        (0x3030, 0x3037),
        (0x303C, 0x303F),
        (0x309B, 0x309C),
        (0x30A0, 0x30A0),
        (0x30FB, 0x30FC),
        (0x3190, 0x319F),
        (0x31C0, 0x31E5),
        (0x31EF, 0x31EF),
        (0x3220, 0x325F),
        (0x327F, 0x32CF),
        (0x32FF, 0x32FF),
        (0x3358, 0x33FF),
        (0x4DC0, 0x4DFF),
        (0xA700, 0xA721),
        (0xA788, 0xA78A),
        (0xA830, 0xA839),
        (0xA92E, 0xA92E),
        (0xA9CF, 0xA9CF),
        (0xAB5B, 0xAB5B),
        (0xAB6A, 0xAB6B),
        (0xFD3E, 0xFD3F),
        (0xFE10, 0xFE19),
        (0xFE30, 0xFE52),
        (0xFE54, 0xFE66),
        (0xFE68, 0xFE6B),
        (0xFEFF, 0xFEFF),
        (0xFF01, 0xFF20),
        (0xFF3B, 0xFF40),
        (0xFF5B, 0xFF65),
        (0xFF70, 0xFF70),
        (0xFF9E, 0xFF9F),
        (0xFFE0, 0xFFE6),
        (0xFFE8, 0xFFEE),
        (0xFFF9, 0xFFFD),
        (0x10100, 0x10102),
        (0x10107, 0x10133),
        (0x10137, 0x1013F),
        (0x10190, 0x1019C),
        (0x101D0, 0x101FC),
        (0x102E1, 0x102FB),
        (0x1BCA0, 0x1BCA3),
        (0x1CC00, 0x1CCFC),
        (0x1CD00, 0x1CEB3),
        (0x1CEBA, 0x1CED0),
        (0x1CEE0, 0x1CEF0),
        (0x1CF50, 0x1CFC3),
        (0x1D000, 0x1D0F5),
        (0x1D100, 0x1D126),
        (0x1D129, 0x1D166),
        (0x1D16A, 0x1D17A),
        (0x1D183, 0x1D184),
        (0x1D18C, 0x1D1A9),
        (0x1D1AE, 0x1D1EA),
        (0x1D2C0, 0x1D2D3),
        (0x1D2E0, 0x1D2F3),
        (0x1D300, 0x1D356),
        (0x1D360, 0x1D378),
        (0x1D400, 0x1D454),
        (0x1D456, 0x1D49C),
        (0x1D49E, 0x1D49F),
        (0x1D4A2, 0x1D4A2),
        (0x1D4A5, 0x1D4A6),
        (0x1D4A9, 0x1D4AC),
        (0x1D4AE, 0x1D4B9),
        (0x1D4BB, 0x1D4BB),
        (0x1D4BD, 0x1D4C3),
        (0x1D4C5, 0x1D505),
        (0x1D507, 0x1D50A),
        (0x1D50D, 0x1D514),
        (0x1D516, 0x1D51C),
        (0x1D51E, 0x1D539),
        (0x1D53B, 0x1D53E),
        (0x1D540, 0x1D544),
        (0x1D546, 0x1D546),
        (0x1D54A, 0x1D550),
        (0x1D552, 0x1D6A5),
        (0x1D6A8, 0x1D7CB),
        (0x1D7CE, 0x1D7FF),
        (0x1EC71, 0x1ECB4),
        (0x1ED01, 0x1ED3D),
        (0x1F000, 0x1F02B),
        (0x1F030, 0x1F093),
        (0x1F0A0, 0x1F0AE),
        (0x1F0B1, 0x1F0BF),
        (0x1F0C1, 0x1F0CF),
        (0x1F0D1, 0x1F0F5),
        (0x1F100, 0x1F1AD),
        (0x1F1E6, 0x1F1FF),
        (0x1F201, 0x1F202),
        (0x1F210, 0x1F23B),
        (0x1F240, 0x1F248),
        (0x1F250, 0x1F251),
        (0x1F260, 0x1F265),
        (0x1F300, 0x1F6D8),
        (0x1F6DC, 0x1F6EC),
        (0x1F6F0, 0x1F6FC),
        (0x1F700, 0x1F7D9),
        (0x1F7E0, 0x1F7EB),
        (0x1F7F0, 0x1F7F0),
        (0x1F800, 0x1F80B),
        (0x1F810, 0x1F847),
        (0x1F850, 0x1F859),
        (0x1F860, 0x1F887),
        (0x1F890, 0x1F8AD),
        (0x1F8B0, 0x1F8BB),
        (0x1F8C0, 0x1F8C1),
        (0x1F8D0, 0x1F8D8),
        (0x1F900, 0x1FA57),
        (0x1FA60, 0x1FA6D),
        (0x1FA70, 0x1FA7C),
        (0x1FA80, 0x1FA8A),
        (0x1FA8E, 0x1FAC6),
        (0x1FAC8, 0x1FAC8),
        (0x1FACD, 0x1FADC),
        (0x1FADF, 0x1FAEA),
        (0x1FAEF, 0x1FAF8),
        (0x1FB00, 0x1FB92),
        (0x1FB94, 0x1FBFA),
        (0xE0001, 0xE0001),
        (0xE0020, 0xE007F)
    ];

    private static readonly (int Start, int End)[] CopticRanges =
    [
        (0x3E2, 0x3EF),
        (0x2C80, 0x2CF3),
        (0x2CF9, 0x2CFF)
    ];

    private static readonly (int Start, int End)[] CuneiformRanges =
    [
        (0x12000, 0x12399),
        (0x12400, 0x1246E),
        (0x12470, 0x12474),
        (0x12480, 0x12543)
    ];

    private static readonly (int Start, int End)[] CypriotRanges =
    [
        (0x10800, 0x10805),
        (0x10808, 0x10808),
        (0x1080A, 0x10835),
        (0x10837, 0x10838),
        (0x1083C, 0x1083C),
        (0x1083F, 0x1083F)
    ];

    private static readonly (int Start, int End)[] CyproMinoanRanges =
    [
        (0x12F90, 0x12FF2)
    ];

    private static readonly (int Start, int End)[] CyrillicRanges =
    [
        (0x400, 0x484),
        (0x487, 0x52F),
        (0x1C80, 0x1C8A),
        (0x1D2B, 0x1D2B),
        (0x1D78, 0x1D78),
        (0x2DE0, 0x2DFF),
        (0xA640, 0xA69F),
        (0xFE2E, 0xFE2F),
        (0x1E030, 0x1E06D),
        (0x1E08F, 0x1E08F)
    ];

    private static readonly (int Start, int End)[] DeseretRanges =
    [
        (0x10400, 0x1044F)
    ];

    private static readonly (int Start, int End)[] DevanagariRanges =
    [
        (0x900, 0x950),
        (0x955, 0x963),
        (0x966, 0x97F),
        (0xA8E0, 0xA8FF),
        (0x11B00, 0x11B09)
    ];

    private static readonly (int Start, int End)[] DivesAkuruRanges =
    [
        (0x11900, 0x11906),
        (0x11909, 0x11909),
        (0x1190C, 0x11913),
        (0x11915, 0x11916),
        (0x11918, 0x11935),
        (0x11937, 0x11938),
        (0x1193B, 0x11946),
        (0x11950, 0x11959)
    ];

    private static readonly (int Start, int End)[] DograRanges =
    [
        (0x11800, 0x1183B)
    ];

    private static readonly (int Start, int End)[] DuployanRanges =
    [
        (0x1BC00, 0x1BC6A),
        (0x1BC70, 0x1BC7C),
        (0x1BC80, 0x1BC88),
        (0x1BC90, 0x1BC99),
        (0x1BC9C, 0x1BC9F)
    ];

    private static readonly (int Start, int End)[] EgyptianHieroglyphsRanges =
    [
        (0x13000, 0x13455),
        (0x13460, 0x143FA)
    ];

    private static readonly (int Start, int End)[] ElbasanRanges =
    [
        (0x10500, 0x10527)
    ];

    private static readonly (int Start, int End)[] ElymaicRanges =
    [
        (0x10FE0, 0x10FF6)
    ];

    private static readonly (int Start, int End)[] EthiopicRanges =
    [
        (0x1200, 0x1248),
        (0x124A, 0x124D),
        (0x1250, 0x1256),
        (0x1258, 0x1258),
        (0x125A, 0x125D),
        (0x1260, 0x1288),
        (0x128A, 0x128D),
        (0x1290, 0x12B0),
        (0x12B2, 0x12B5),
        (0x12B8, 0x12BE),
        (0x12C0, 0x12C0),
        (0x12C2, 0x12C5),
        (0x12C8, 0x12D6),
        (0x12D8, 0x1310),
        (0x1312, 0x1315),
        (0x1318, 0x135A),
        (0x135D, 0x137C),
        (0x1380, 0x1399),
        (0x2D80, 0x2D96),
        (0x2DA0, 0x2DA6),
        (0x2DA8, 0x2DAE),
        (0x2DB0, 0x2DB6),
        (0x2DB8, 0x2DBE),
        (0x2DC0, 0x2DC6),
        (0x2DC8, 0x2DCE),
        (0x2DD0, 0x2DD6),
        (0x2DD8, 0x2DDE),
        (0xAB01, 0xAB06),
        (0xAB09, 0xAB0E),
        (0xAB11, 0xAB16),
        (0xAB20, 0xAB26),
        (0xAB28, 0xAB2E),
        (0x1E7E0, 0x1E7E6),
        (0x1E7E8, 0x1E7EB),
        (0x1E7ED, 0x1E7EE),
        (0x1E7F0, 0x1E7FE)
    ];

    private static readonly (int Start, int End)[] GarayRanges =
    [
        (0x10D40, 0x10D65),
        (0x10D69, 0x10D85),
        (0x10D8E, 0x10D8F)
    ];

    private static readonly (int Start, int End)[] GeorgianRanges =
    [
        (0x10A0, 0x10C5),
        (0x10C7, 0x10C7),
        (0x10CD, 0x10CD),
        (0x10D0, 0x10FA),
        (0x10FC, 0x10FF),
        (0x1C90, 0x1CBA),
        (0x1CBD, 0x1CBF),
        (0x2D00, 0x2D25),
        (0x2D27, 0x2D27),
        (0x2D2D, 0x2D2D)
    ];

    private static readonly (int Start, int End)[] GlagoliticRanges =
    [
        (0x2C00, 0x2C5F),
        (0x1E000, 0x1E006),
        (0x1E008, 0x1E018),
        (0x1E01B, 0x1E021),
        (0x1E023, 0x1E024),
        (0x1E026, 0x1E02A)
    ];

    private static readonly (int Start, int End)[] GothicRanges =
    [
        (0x10330, 0x1034A)
    ];

    private static readonly (int Start, int End)[] GranthaRanges =
    [
        (0x11300, 0x11303),
        (0x11305, 0x1130C),
        (0x1130F, 0x11310),
        (0x11313, 0x11328),
        (0x1132A, 0x11330),
        (0x11332, 0x11333),
        (0x11335, 0x11339),
        (0x1133C, 0x11344),
        (0x11347, 0x11348),
        (0x1134B, 0x1134D),
        (0x11350, 0x11350),
        (0x11357, 0x11357),
        (0x1135D, 0x11363),
        (0x11366, 0x1136C),
        (0x11370, 0x11374)
    ];

    private static readonly (int Start, int End)[] GreekRanges =
    [
        (0x370, 0x373),
        (0x375, 0x377),
        (0x37A, 0x37D),
        (0x37F, 0x37F),
        (0x384, 0x384),
        (0x386, 0x386),
        (0x388, 0x38A),
        (0x38C, 0x38C),
        (0x38E, 0x3A1),
        (0x3A3, 0x3E1),
        (0x3F0, 0x3FF),
        (0x1D26, 0x1D2A),
        (0x1D5D, 0x1D61),
        (0x1D66, 0x1D6A),
        (0x1DBF, 0x1DBF),
        (0x1F00, 0x1F15),
        (0x1F18, 0x1F1D),
        (0x1F20, 0x1F45),
        (0x1F48, 0x1F4D),
        (0x1F50, 0x1F57),
        (0x1F59, 0x1F59),
        (0x1F5B, 0x1F5B),
        (0x1F5D, 0x1F5D),
        (0x1F5F, 0x1F7D),
        (0x1F80, 0x1FB4),
        (0x1FB6, 0x1FC4),
        (0x1FC6, 0x1FD3),
        (0x1FD6, 0x1FDB),
        (0x1FDD, 0x1FEF),
        (0x1FF2, 0x1FF4),
        (0x1FF6, 0x1FFE),
        (0x2126, 0x2126),
        (0xAB65, 0xAB65),
        (0x10140, 0x1018E),
        (0x101A0, 0x101A0),
        (0x1D200, 0x1D245)
    ];

    private static readonly (int Start, int End)[] GujaratiRanges =
    [
        (0xA81, 0xA83),
        (0xA85, 0xA8D),
        (0xA8F, 0xA91),
        (0xA93, 0xAA8),
        (0xAAA, 0xAB0),
        (0xAB2, 0xAB3),
        (0xAB5, 0xAB9),
        (0xABC, 0xAC5),
        (0xAC7, 0xAC9),
        (0xACB, 0xACD),
        (0xAD0, 0xAD0),
        (0xAE0, 0xAE3),
        (0xAE6, 0xAF1),
        (0xAF9, 0xAFF)
    ];

    private static readonly (int Start, int End)[] GunjalaGondiRanges =
    [
        (0x11D60, 0x11D65),
        (0x11D67, 0x11D68),
        (0x11D6A, 0x11D8E),
        (0x11D90, 0x11D91),
        (0x11D93, 0x11D98),
        (0x11DA0, 0x11DA9)
    ];

    private static readonly (int Start, int End)[] GurmukhiRanges =
    [
        (0xA01, 0xA03),
        (0xA05, 0xA0A),
        (0xA0F, 0xA10),
        (0xA13, 0xA28),
        (0xA2A, 0xA30),
        (0xA32, 0xA33),
        (0xA35, 0xA36),
        (0xA38, 0xA39),
        (0xA3C, 0xA3C),
        (0xA3E, 0xA42),
        (0xA47, 0xA48),
        (0xA4B, 0xA4D),
        (0xA51, 0xA51),
        (0xA59, 0xA5C),
        (0xA5E, 0xA5E),
        (0xA66, 0xA76)
    ];

    private static readonly (int Start, int End)[] GurungKhemaRanges =
    [
        (0x16100, 0x16139)
    ];

    private static readonly (int Start, int End)[] HanRanges =
    [
        (0x2E80, 0x2E99),
        (0x2E9B, 0x2EF3),
        (0x2F00, 0x2FD5),
        (0x3005, 0x3005),
        (0x3007, 0x3007),
        (0x3021, 0x3029),
        (0x3038, 0x303B),
        (0x3400, 0x4DBF),
        (0x4E00, 0x9FFF),
        (0xF900, 0xFA6D),
        (0xFA70, 0xFAD9),
        (0x16FE2, 0x16FE3),
        (0x16FF0, 0x16FF6),
        (0x20000, 0x2A6DF),
        (0x2A700, 0x2B81D),
        (0x2B820, 0x2CEAD),
        (0x2CEB0, 0x2EBE0),
        (0x2EBF0, 0x2EE5D),
        (0x2F800, 0x2FA1D),
        (0x30000, 0x3134A),
        (0x31350, 0x33479)
    ];

    private static readonly (int Start, int End)[] HangulRanges =
    [
        (0x1100, 0x11FF),
        (0x302E, 0x302F),
        (0x3131, 0x318E),
        (0x3200, 0x321E),
        (0x3260, 0x327E),
        (0xA960, 0xA97C),
        (0xAC00, 0xD7A3),
        (0xD7B0, 0xD7C6),
        (0xD7CB, 0xD7FB),
        (0xFFA0, 0xFFBE),
        (0xFFC2, 0xFFC7),
        (0xFFCA, 0xFFCF),
        (0xFFD2, 0xFFD7),
        (0xFFDA, 0xFFDC)
    ];

    private static readonly (int Start, int End)[] HanifiRohingyaRanges =
    [
        (0x10D00, 0x10D27),
        (0x10D30, 0x10D39)
    ];

    private static readonly (int Start, int End)[] HanunooRanges =
    [
        (0x1720, 0x1734)
    ];

    private static readonly (int Start, int End)[] HatranRanges =
    [
        (0x108E0, 0x108F2),
        (0x108F4, 0x108F5),
        (0x108FB, 0x108FF)
    ];

    private static readonly (int Start, int End)[] HebrewRanges =
    [
        (0x591, 0x5C7),
        (0x5D0, 0x5EA),
        (0x5EF, 0x5F4),
        (0xFB1D, 0xFB36),
        (0xFB38, 0xFB3C),
        (0xFB3E, 0xFB3E),
        (0xFB40, 0xFB41),
        (0xFB43, 0xFB44),
        (0xFB46, 0xFB4F)
    ];

    private static readonly (int Start, int End)[] HiraganaRanges =
    [
        (0x3041, 0x3096),
        (0x309D, 0x309F),
        (0x1B001, 0x1B11F),
        (0x1B132, 0x1B132),
        (0x1B150, 0x1B152),
        (0x1F200, 0x1F200)
    ];

    private static readonly (int Start, int End)[] ImperialAramaicRanges =
    [
        (0x10840, 0x10855),
        (0x10857, 0x1085F)
    ];

    private static readonly (int Start, int End)[] InheritedRanges =
    [
        (0x300, 0x36F),
        (0x485, 0x486),
        (0x64B, 0x655),
        (0x670, 0x670),
        (0x951, 0x954),
        (0x1AB0, 0x1ADD),
        (0x1AE0, 0x1AEB),
        (0x1CD0, 0x1CD2),
        (0x1CD4, 0x1CE0),
        (0x1CE2, 0x1CE8),
        (0x1CED, 0x1CED),
        (0x1CF4, 0x1CF4),
        (0x1CF8, 0x1CF9),
        (0x1DC0, 0x1DFF),
        (0x200C, 0x200D),
        (0x20D0, 0x20F0),
        (0x302A, 0x302D),
        (0x3099, 0x309A),
        (0xFE00, 0xFE0F),
        (0xFE20, 0xFE2D),
        (0x101FD, 0x101FD),
        (0x102E0, 0x102E0),
        (0x1133B, 0x1133B),
        (0x1CF00, 0x1CF2D),
        (0x1CF30, 0x1CF46),
        (0x1D167, 0x1D169),
        (0x1D17B, 0x1D182),
        (0x1D185, 0x1D18B),
        (0x1D1AA, 0x1D1AD),
        (0xE0100, 0xE01EF)
    ];

    private static readonly (int Start, int End)[] InscriptionalPahlaviRanges =
    [
        (0x10B60, 0x10B72),
        (0x10B78, 0x10B7F)
    ];

    private static readonly (int Start, int End)[] InscriptionalParthianRanges =
    [
        (0x10B40, 0x10B55),
        (0x10B58, 0x10B5F)
    ];

    private static readonly (int Start, int End)[] JavaneseRanges =
    [
        (0xA980, 0xA9CD),
        (0xA9D0, 0xA9D9),
        (0xA9DE, 0xA9DF)
    ];

    private static readonly (int Start, int End)[] KaithiRanges =
    [
        (0x11080, 0x110C2),
        (0x110CD, 0x110CD)
    ];

    private static readonly (int Start, int End)[] KannadaRanges =
    [
        (0xC80, 0xC8C),
        (0xC8E, 0xC90),
        (0xC92, 0xCA8),
        (0xCAA, 0xCB3),
        (0xCB5, 0xCB9),
        (0xCBC, 0xCC4),
        (0xCC6, 0xCC8),
        (0xCCA, 0xCCD),
        (0xCD5, 0xCD6),
        (0xCDC, 0xCDE),
        (0xCE0, 0xCE3),
        (0xCE6, 0xCEF),
        (0xCF1, 0xCF3)
    ];

    private static readonly (int Start, int End)[] KatakanaRanges =
    [
        (0x30A1, 0x30FA),
        (0x30FD, 0x30FF),
        (0x31F0, 0x31FF),
        (0x32D0, 0x32FE),
        (0x3300, 0x3357),
        (0xFF66, 0xFF6F),
        (0xFF71, 0xFF9D),
        (0x1AFF0, 0x1AFF3),
        (0x1AFF5, 0x1AFFB),
        (0x1AFFD, 0x1AFFE),
        (0x1B000, 0x1B000),
        (0x1B120, 0x1B122),
        (0x1B155, 0x1B155),
        (0x1B164, 0x1B167)
    ];

    private static readonly (int Start, int End)[] KawiRanges =
    [
        (0x11F00, 0x11F10),
        (0x11F12, 0x11F3A),
        (0x11F3E, 0x11F5A)
    ];

    private static readonly (int Start, int End)[] KayahLiRanges =
    [
        (0xA900, 0xA92D),
        (0xA92F, 0xA92F)
    ];

    private static readonly (int Start, int End)[] KharoshthiRanges =
    [
        (0x10A00, 0x10A03),
        (0x10A05, 0x10A06),
        (0x10A0C, 0x10A13),
        (0x10A15, 0x10A17),
        (0x10A19, 0x10A35),
        (0x10A38, 0x10A3A),
        (0x10A3F, 0x10A48),
        (0x10A50, 0x10A58)
    ];

    private static readonly (int Start, int End)[] KhitanSmallScriptRanges =
    [
        (0x16FE4, 0x16FE4),
        (0x18B00, 0x18CD5),
        (0x18CFF, 0x18CFF)
    ];

    private static readonly (int Start, int End)[] KhmerRanges =
    [
        (0x1780, 0x17DD),
        (0x17E0, 0x17E9),
        (0x17F0, 0x17F9),
        (0x19E0, 0x19FF)
    ];

    private static readonly (int Start, int End)[] KhojkiRanges =
    [
        (0x11200, 0x11211),
        (0x11213, 0x11241)
    ];

    private static readonly (int Start, int End)[] KhudawadiRanges =
    [
        (0x112B0, 0x112EA),
        (0x112F0, 0x112F9)
    ];

    private static readonly (int Start, int End)[] KiratRaiRanges =
    [
        (0x16D40, 0x16D79)
    ];

    private static readonly (int Start, int End)[] LaoRanges =
    [
        (0xE81, 0xE82),
        (0xE84, 0xE84),
        (0xE86, 0xE8A),
        (0xE8C, 0xEA3),
        (0xEA5, 0xEA5),
        (0xEA7, 0xEBD),
        (0xEC0, 0xEC4),
        (0xEC6, 0xEC6),
        (0xEC8, 0xECE),
        (0xED0, 0xED9),
        (0xEDC, 0xEDF)
    ];

    private static readonly (int Start, int End)[] LatinRanges =
    [
        (0x41, 0x5A),
        (0x61, 0x7A),
        (0xAA, 0xAA),
        (0xBA, 0xBA),
        (0xC0, 0xD6),
        (0xD8, 0xF6),
        (0xF8, 0x2B8),
        (0x2E0, 0x2E4),
        (0x1D00, 0x1D25),
        (0x1D2C, 0x1D5C),
        (0x1D62, 0x1D65),
        (0x1D6B, 0x1D77),
        (0x1D79, 0x1DBE),
        (0x1E00, 0x1EFF),
        (0x2071, 0x2071),
        (0x207F, 0x207F),
        (0x2090, 0x209C),
        (0x212A, 0x212B),
        (0x2132, 0x2132),
        (0x214E, 0x214E),
        (0x2160, 0x2188),
        (0x2C60, 0x2C7F),
        (0xA722, 0xA787),
        (0xA78B, 0xA7DC),
        (0xA7F1, 0xA7FF),
        (0xAB30, 0xAB5A),
        (0xAB5C, 0xAB64),
        (0xAB66, 0xAB69),
        (0xFB00, 0xFB06),
        (0xFF21, 0xFF3A),
        (0xFF41, 0xFF5A),
        (0x10780, 0x10785),
        (0x10787, 0x107B0),
        (0x107B2, 0x107BA),
        (0x1DF00, 0x1DF1E),
        (0x1DF25, 0x1DF2A)
    ];

    private static readonly (int Start, int End)[] LepchaRanges =
    [
        (0x1C00, 0x1C37),
        (0x1C3B, 0x1C49),
        (0x1C4D, 0x1C4F)
    ];

    private static readonly (int Start, int End)[] LimbuRanges =
    [
        (0x1900, 0x191E),
        (0x1920, 0x192B),
        (0x1930, 0x193B),
        (0x1940, 0x1940),
        (0x1944, 0x194F)
    ];

    private static readonly (int Start, int End)[] LinearARanges =
    [
        (0x10600, 0x10736),
        (0x10740, 0x10755),
        (0x10760, 0x10767)
    ];

    private static readonly (int Start, int End)[] LinearBRanges =
    [
        (0x10000, 0x1000B),
        (0x1000D, 0x10026),
        (0x10028, 0x1003A),
        (0x1003C, 0x1003D),
        (0x1003F, 0x1004D),
        (0x10050, 0x1005D),
        (0x10080, 0x100FA)
    ];

    private static readonly (int Start, int End)[] LisuRanges =
    [
        (0xA4D0, 0xA4FF),
        (0x11FB0, 0x11FB0)
    ];

    private static readonly (int Start, int End)[] LycianRanges =
    [
        (0x10280, 0x1029C)
    ];

    private static readonly (int Start, int End)[] LydianRanges =
    [
        (0x10920, 0x10939),
        (0x1093F, 0x1093F)
    ];

    private static readonly (int Start, int End)[] MahajaniRanges =
    [
        (0x11150, 0x11176)
    ];

    private static readonly (int Start, int End)[] MakasarRanges =
    [
        (0x11EE0, 0x11EF8)
    ];

    private static readonly (int Start, int End)[] MalayalamRanges =
    [
        (0xD00, 0xD0C),
        (0xD0E, 0xD10),
        (0xD12, 0xD44),
        (0xD46, 0xD48),
        (0xD4A, 0xD4F),
        (0xD54, 0xD63),
        (0xD66, 0xD7F)
    ];

    private static readonly (int Start, int End)[] MandaicRanges =
    [
        (0x840, 0x85B),
        (0x85E, 0x85E)
    ];

    private static readonly (int Start, int End)[] ManichaeanRanges =
    [
        (0x10AC0, 0x10AE6),
        (0x10AEB, 0x10AF6)
    ];

    private static readonly (int Start, int End)[] MarchenRanges =
    [
        (0x11C70, 0x11C8F),
        (0x11C92, 0x11CA7),
        (0x11CA9, 0x11CB6)
    ];

    private static readonly (int Start, int End)[] MasaramGondiRanges =
    [
        (0x11D00, 0x11D06),
        (0x11D08, 0x11D09),
        (0x11D0B, 0x11D36),
        (0x11D3A, 0x11D3A),
        (0x11D3C, 0x11D3D),
        (0x11D3F, 0x11D47),
        (0x11D50, 0x11D59)
    ];

    private static readonly (int Start, int End)[] MedefaidrinRanges =
    [
        (0x16E40, 0x16E9A)
    ];

    private static readonly (int Start, int End)[] MeeteiMayekRanges =
    [
        (0xAAE0, 0xAAF6),
        (0xABC0, 0xABED),
        (0xABF0, 0xABF9)
    ];

    private static readonly (int Start, int End)[] MendeKikakuiRanges =
    [
        (0x1E800, 0x1E8C4),
        (0x1E8C7, 0x1E8D6)
    ];

    private static readonly (int Start, int End)[] MeroiticCursiveRanges =
    [
        (0x109A0, 0x109B7),
        (0x109BC, 0x109CF),
        (0x109D2, 0x109FF)
    ];

    private static readonly (int Start, int End)[] MeroiticHieroglyphsRanges =
    [
        (0x10980, 0x1099F)
    ];

    private static readonly (int Start, int End)[] MiaoRanges =
    [
        (0x16F00, 0x16F4A),
        (0x16F4F, 0x16F87),
        (0x16F8F, 0x16F9F)
    ];

    private static readonly (int Start, int End)[] ModiRanges =
    [
        (0x11600, 0x11644),
        (0x11650, 0x11659)
    ];

    private static readonly (int Start, int End)[] MongolianRanges =
    [
        (0x1800, 0x1801),
        (0x1804, 0x1804),
        (0x1806, 0x1819),
        (0x1820, 0x1878),
        (0x1880, 0x18AA),
        (0x11660, 0x1166C)
    ];

    private static readonly (int Start, int End)[] MroRanges =
    [
        (0x16A40, 0x16A5E),
        (0x16A60, 0x16A69),
        (0x16A6E, 0x16A6F)
    ];

    private static readonly (int Start, int End)[] MultaniRanges =
    [
        (0x11280, 0x11286),
        (0x11288, 0x11288),
        (0x1128A, 0x1128D),
        (0x1128F, 0x1129D),
        (0x1129F, 0x112A9)
    ];

    private static readonly (int Start, int End)[] MyanmarRanges =
    [
        (0x1000, 0x109F),
        (0xA9E0, 0xA9FE),
        (0xAA60, 0xAA7F),
        (0x116D0, 0x116E3)
    ];

    private static readonly (int Start, int End)[] NabataeanRanges =
    [
        (0x10880, 0x1089E),
        (0x108A7, 0x108AF)
    ];

    private static readonly (int Start, int End)[] NagMundariRanges =
    [
        (0x1E4D0, 0x1E4F9)
    ];

    private static readonly (int Start, int End)[] NandinagariRanges =
    [
        (0x119A0, 0x119A7),
        (0x119AA, 0x119D7),
        (0x119DA, 0x119E4)
    ];

    private static readonly (int Start, int End)[] NewTaiLueRanges =
    [
        (0x1980, 0x19AB),
        (0x19B0, 0x19C9),
        (0x19D0, 0x19DA),
        (0x19DE, 0x19DF)
    ];

    private static readonly (int Start, int End)[] NewaRanges =
    [
        (0x11400, 0x1145B),
        (0x1145D, 0x11461)
    ];

    private static readonly (int Start, int End)[] NkoRanges =
    [
        (0x7C0, 0x7FA),
        (0x7FD, 0x7FF)
    ];

    private static readonly (int Start, int End)[] NushuRanges =
    [
        (0x16FE1, 0x16FE1),
        (0x1B170, 0x1B2FB)
    ];

    private static readonly (int Start, int End)[] NyiakengPuachueHmongRanges =
    [
        (0x1E100, 0x1E12C),
        (0x1E130, 0x1E13D),
        (0x1E140, 0x1E149),
        (0x1E14E, 0x1E14F)
    ];

    private static readonly (int Start, int End)[] OghamRanges =
    [
        (0x1680, 0x169C)
    ];

    private static readonly (int Start, int End)[] OlChikiRanges =
    [
        (0x1C50, 0x1C7F)
    ];

    private static readonly (int Start, int End)[] OlOnalRanges =
    [
        (0x1E5D0, 0x1E5FA),
        (0x1E5FF, 0x1E5FF)
    ];

    private static readonly (int Start, int End)[] OldHungarianRanges =
    [
        (0x10C80, 0x10CB2),
        (0x10CC0, 0x10CF2),
        (0x10CFA, 0x10CFF)
    ];

    private static readonly (int Start, int End)[] OldItalicRanges =
    [
        (0x10300, 0x10323),
        (0x1032D, 0x1032F)
    ];

    private static readonly (int Start, int End)[] OldNorthArabianRanges =
    [
        (0x10A80, 0x10A9F)
    ];

    private static readonly (int Start, int End)[] OldPermicRanges =
    [
        (0x10350, 0x1037A)
    ];

    private static readonly (int Start, int End)[] OldPersianRanges =
    [
        (0x103A0, 0x103C3),
        (0x103C8, 0x103D5)
    ];

    private static readonly (int Start, int End)[] OldSogdianRanges =
    [
        (0x10F00, 0x10F27)
    ];

    private static readonly (int Start, int End)[] OldSouthArabianRanges =
    [
        (0x10A60, 0x10A7F)
    ];

    private static readonly (int Start, int End)[] OldTurkicRanges =
    [
        (0x10C00, 0x10C48)
    ];

    private static readonly (int Start, int End)[] OldUyghurRanges =
    [
        (0x10F70, 0x10F89)
    ];

    private static readonly (int Start, int End)[] OriyaRanges =
    [
        (0xB01, 0xB03),
        (0xB05, 0xB0C),
        (0xB0F, 0xB10),
        (0xB13, 0xB28),
        (0xB2A, 0xB30),
        (0xB32, 0xB33),
        (0xB35, 0xB39),
        (0xB3C, 0xB44),
        (0xB47, 0xB48),
        (0xB4B, 0xB4D),
        (0xB55, 0xB57),
        (0xB5C, 0xB5D),
        (0xB5F, 0xB63),
        (0xB66, 0xB77)
    ];

    private static readonly (int Start, int End)[] OsageRanges =
    [
        (0x104B0, 0x104D3),
        (0x104D8, 0x104FB)
    ];

    private static readonly (int Start, int End)[] OsmanyaRanges =
    [
        (0x10480, 0x1049D),
        (0x104A0, 0x104A9)
    ];

    private static readonly (int Start, int End)[] PahawhHmongRanges =
    [
        (0x16B00, 0x16B45),
        (0x16B50, 0x16B59),
        (0x16B5B, 0x16B61),
        (0x16B63, 0x16B77),
        (0x16B7D, 0x16B8F)
    ];

    private static readonly (int Start, int End)[] PalmyreneRanges =
    [
        (0x10860, 0x1087F)
    ];

    private static readonly (int Start, int End)[] PauCinHauRanges =
    [
        (0x11AC0, 0x11AF8)
    ];

    private static readonly (int Start, int End)[] PhagsPaRanges =
    [
        (0xA840, 0xA877)
    ];

    private static readonly (int Start, int End)[] PhoenicianRanges =
    [
        (0x10900, 0x1091B),
        (0x1091F, 0x1091F)
    ];

    private static readonly (int Start, int End)[] PsalterPahlaviRanges =
    [
        (0x10B80, 0x10B91),
        (0x10B99, 0x10B9C),
        (0x10BA9, 0x10BAF)
    ];

    private static readonly (int Start, int End)[] RejangRanges =
    [
        (0xA930, 0xA953),
        (0xA95F, 0xA95F)
    ];

    private static readonly (int Start, int End)[] RunicRanges =
    [
        (0x16A0, 0x16EA),
        (0x16EE, 0x16F8)
    ];

    private static readonly (int Start, int End)[] SamaritanRanges =
    [
        (0x800, 0x82D),
        (0x830, 0x83E)
    ];

    private static readonly (int Start, int End)[] SaurashtraRanges =
    [
        (0xA880, 0xA8C5),
        (0xA8CE, 0xA8D9)
    ];

    private static readonly (int Start, int End)[] SharadaRanges =
    [
        (0x11180, 0x111DF),
        (0x11B60, 0x11B67)
    ];

    private static readonly (int Start, int End)[] ShavianRanges =
    [
        (0x10450, 0x1047F)
    ];

    private static readonly (int Start, int End)[] SiddhamRanges =
    [
        (0x11580, 0x115B5),
        (0x115B8, 0x115DD)
    ];

    private static readonly (int Start, int End)[] SideticRanges =
    [
        (0x10940, 0x10959)
    ];

    private static readonly (int Start, int End)[] SignWritingRanges =
    [
        (0x1D800, 0x1DA8B),
        (0x1DA9B, 0x1DA9F),
        (0x1DAA1, 0x1DAAF)
    ];

    private static readonly (int Start, int End)[] SinhalaRanges =
    [
        (0xD81, 0xD83),
        (0xD85, 0xD96),
        (0xD9A, 0xDB1),
        (0xDB3, 0xDBB),
        (0xDBD, 0xDBD),
        (0xDC0, 0xDC6),
        (0xDCA, 0xDCA),
        (0xDCF, 0xDD4),
        (0xDD6, 0xDD6),
        (0xDD8, 0xDDF),
        (0xDE6, 0xDEF),
        (0xDF2, 0xDF4),
        (0x111E1, 0x111F4)
    ];

    private static readonly (int Start, int End)[] SogdianRanges =
    [
        (0x10F30, 0x10F59)
    ];

    private static readonly (int Start, int End)[] SoraSompengRanges =
    [
        (0x110D0, 0x110E8),
        (0x110F0, 0x110F9)
    ];

    private static readonly (int Start, int End)[] SoyomboRanges =
    [
        (0x11A50, 0x11AA2)
    ];

    private static readonly (int Start, int End)[] SundaneseRanges =
    [
        (0x1B80, 0x1BBF),
        (0x1CC0, 0x1CC7)
    ];

    private static readonly (int Start, int End)[] SunuwarRanges =
    [
        (0x11BC0, 0x11BE1),
        (0x11BF0, 0x11BF9)
    ];

    private static readonly (int Start, int End)[] SylotiNagriRanges =
    [
        (0xA800, 0xA82C)
    ];

    private static readonly (int Start, int End)[] SyriacRanges =
    [
        (0x700, 0x70D),
        (0x70F, 0x74A),
        (0x74D, 0x74F),
        (0x860, 0x86A)
    ];

    private static readonly (int Start, int End)[] TagalogRanges =
    [
        (0x1700, 0x1715),
        (0x171F, 0x171F)
    ];

    private static readonly (int Start, int End)[] TagbanwaRanges =
    [
        (0x1760, 0x176C),
        (0x176E, 0x1770),
        (0x1772, 0x1773)
    ];

    private static readonly (int Start, int End)[] TaiLeRanges =
    [
        (0x1950, 0x196D),
        (0x1970, 0x1974)
    ];

    private static readonly (int Start, int End)[] TaiThamRanges =
    [
        (0x1A20, 0x1A5E),
        (0x1A60, 0x1A7C),
        (0x1A7F, 0x1A89),
        (0x1A90, 0x1A99),
        (0x1AA0, 0x1AAD)
    ];

    private static readonly (int Start, int End)[] TaiVietRanges =
    [
        (0xAA80, 0xAAC2),
        (0xAADB, 0xAADF)
    ];

    private static readonly (int Start, int End)[] TaiYoRanges =
    [
        (0x1E6C0, 0x1E6DE),
        (0x1E6E0, 0x1E6F5),
        (0x1E6FE, 0x1E6FF)
    ];

    private static readonly (int Start, int End)[] TakriRanges =
    [
        (0x11680, 0x116B9),
        (0x116C0, 0x116C9)
    ];

    private static readonly (int Start, int End)[] TamilRanges =
    [
        (0xB82, 0xB83),
        (0xB85, 0xB8A),
        (0xB8E, 0xB90),
        (0xB92, 0xB95),
        (0xB99, 0xB9A),
        (0xB9C, 0xB9C),
        (0xB9E, 0xB9F),
        (0xBA3, 0xBA4),
        (0xBA8, 0xBAA),
        (0xBAE, 0xBB9),
        (0xBBE, 0xBC2),
        (0xBC6, 0xBC8),
        (0xBCA, 0xBCD),
        (0xBD0, 0xBD0),
        (0xBD7, 0xBD7),
        (0xBE6, 0xBFA),
        (0x11FC0, 0x11FF1),
        (0x11FFF, 0x11FFF)
    ];

    private static readonly (int Start, int End)[] TangsaRanges =
    [
        (0x16A70, 0x16ABE),
        (0x16AC0, 0x16AC9)
    ];

    private static readonly (int Start, int End)[] TangutRanges =
    [
        (0x16FE0, 0x16FE0),
        (0x17000, 0x18AFF),
        (0x18D00, 0x18D1E),
        (0x18D80, 0x18DF2)
    ];

    private static readonly (int Start, int End)[] TeluguRanges =
    [
        (0xC00, 0xC0C),
        (0xC0E, 0xC10),
        (0xC12, 0xC28),
        (0xC2A, 0xC39),
        (0xC3C, 0xC44),
        (0xC46, 0xC48),
        (0xC4A, 0xC4D),
        (0xC55, 0xC56),
        (0xC58, 0xC5A),
        (0xC5C, 0xC5D),
        (0xC60, 0xC63),
        (0xC66, 0xC6F),
        (0xC77, 0xC7F)
    ];

    private static readonly (int Start, int End)[] ThaanaRanges =
    [
        (0x780, 0x7B1)
    ];

    private static readonly (int Start, int End)[] ThaiRanges =
    [
        (0xE01, 0xE3A),
        (0xE40, 0xE5B)
    ];

    private static readonly (int Start, int End)[] TibetanRanges =
    [
        (0xF00, 0xF47),
        (0xF49, 0xF6C),
        (0xF71, 0xF97),
        (0xF99, 0xFBC),
        (0xFBE, 0xFCC),
        (0xFCE, 0xFD4),
        (0xFD9, 0xFDA)
    ];

    private static readonly (int Start, int End)[] TifinaghRanges =
    [
        (0x2D30, 0x2D67),
        (0x2D6F, 0x2D70),
        (0x2D7F, 0x2D7F)
    ];

    private static readonly (int Start, int End)[] TirhutaRanges =
    [
        (0x11480, 0x114C7),
        (0x114D0, 0x114D9)
    ];

    private static readonly (int Start, int End)[] TodhriRanges =
    [
        (0x105C0, 0x105F3)
    ];

    private static readonly (int Start, int End)[] TolongSikiRanges =
    [
        (0x11DB0, 0x11DDB),
        (0x11DE0, 0x11DE9)
    ];

    private static readonly (int Start, int End)[] TotoRanges =
    [
        (0x1E290, 0x1E2AE)
    ];

    private static readonly (int Start, int End)[] TuluTigalariRanges =
    [
        (0x11380, 0x11389),
        (0x1138B, 0x1138B),
        (0x1138E, 0x1138E),
        (0x11390, 0x113B5),
        (0x113B7, 0x113C0),
        (0x113C2, 0x113C2),
        (0x113C5, 0x113C5),
        (0x113C7, 0x113CA),
        (0x113CC, 0x113D5),
        (0x113D7, 0x113D8),
        (0x113E1, 0x113E2)
    ];

    private static readonly (int Start, int End)[] UgariticRanges =
    [
        (0x10380, 0x1039D),
        (0x1039F, 0x1039F)
    ];

    private static readonly (int Start, int End)[] UnknownRanges =
    [
        (0x378, 0x379),
        (0x380, 0x383),
        (0x38B, 0x38B),
        (0x38D, 0x38D),
        (0x3A2, 0x3A2),
        (0x530, 0x530),
        (0x557, 0x558),
        (0x58B, 0x58C),
        (0x590, 0x590),
        (0x5C8, 0x5CF),
        (0x5EB, 0x5EE),
        (0x5F5, 0x5FF),
        (0x70E, 0x70E),
        (0x74B, 0x74C),
        (0x7B2, 0x7BF),
        (0x7FB, 0x7FC),
        (0x82E, 0x82F),
        (0x83F, 0x83F),
        (0x85C, 0x85D),
        (0x85F, 0x85F),
        (0x86B, 0x86F),
        (0x892, 0x896),
        (0x984, 0x984),
        (0x98D, 0x98E),
        (0x991, 0x992),
        (0x9A9, 0x9A9),
        (0x9B1, 0x9B1),
        (0x9B3, 0x9B5),
        (0x9BA, 0x9BB),
        (0x9C5, 0x9C6),
        (0x9C9, 0x9CA),
        (0x9CF, 0x9D6),
        (0x9D8, 0x9DB),
        (0x9DE, 0x9DE),
        (0x9E4, 0x9E5),
        (0x9FF, 0xA00),
        (0xA04, 0xA04),
        (0xA0B, 0xA0E),
        (0xA11, 0xA12),
        (0xA29, 0xA29),
        (0xA31, 0xA31),
        (0xA34, 0xA34),
        (0xA37, 0xA37),
        (0xA3A, 0xA3B),
        (0xA3D, 0xA3D),
        (0xA43, 0xA46),
        (0xA49, 0xA4A),
        (0xA4E, 0xA50),
        (0xA52, 0xA58),
        (0xA5D, 0xA5D),
        (0xA5F, 0xA65),
        (0xA77, 0xA80),
        (0xA84, 0xA84),
        (0xA8E, 0xA8E),
        (0xA92, 0xA92),
        (0xAA9, 0xAA9),
        (0xAB1, 0xAB1),
        (0xAB4, 0xAB4),
        (0xABA, 0xABB),
        (0xAC6, 0xAC6),
        (0xACA, 0xACA),
        (0xACE, 0xACF),
        (0xAD1, 0xADF),
        (0xAE4, 0xAE5),
        (0xAF2, 0xAF8),
        (0xB00, 0xB00),
        (0xB04, 0xB04),
        (0xB0D, 0xB0E),
        (0xB11, 0xB12),
        (0xB29, 0xB29),
        (0xB31, 0xB31),
        (0xB34, 0xB34),
        (0xB3A, 0xB3B),
        (0xB45, 0xB46),
        (0xB49, 0xB4A),
        (0xB4E, 0xB54),
        (0xB58, 0xB5B),
        (0xB5E, 0xB5E),
        (0xB64, 0xB65),
        (0xB78, 0xB81),
        (0xB84, 0xB84),
        (0xB8B, 0xB8D),
        (0xB91, 0xB91),
        (0xB96, 0xB98),
        (0xB9B, 0xB9B),
        (0xB9D, 0xB9D),
        (0xBA0, 0xBA2),
        (0xBA5, 0xBA7),
        (0xBAB, 0xBAD),
        (0xBBA, 0xBBD),
        (0xBC3, 0xBC5),
        (0xBC9, 0xBC9),
        (0xBCE, 0xBCF),
        (0xBD1, 0xBD6),
        (0xBD8, 0xBE5),
        (0xBFB, 0xBFF),
        (0xC0D, 0xC0D),
        (0xC11, 0xC11),
        (0xC29, 0xC29),
        (0xC3A, 0xC3B),
        (0xC45, 0xC45),
        (0xC49, 0xC49),
        (0xC4E, 0xC54),
        (0xC57, 0xC57),
        (0xC5B, 0xC5B),
        (0xC5E, 0xC5F),
        (0xC64, 0xC65),
        (0xC70, 0xC76),
        (0xC8D, 0xC8D),
        (0xC91, 0xC91),
        (0xCA9, 0xCA9),
        (0xCB4, 0xCB4),
        (0xCBA, 0xCBB),
        (0xCC5, 0xCC5),
        (0xCC9, 0xCC9),
        (0xCCE, 0xCD4),
        (0xCD7, 0xCDB),
        (0xCDF, 0xCDF),
        (0xCE4, 0xCE5),
        (0xCF0, 0xCF0),
        (0xCF4, 0xCFF),
        (0xD0D, 0xD0D),
        (0xD11, 0xD11),
        (0xD45, 0xD45),
        (0xD49, 0xD49),
        (0xD50, 0xD53),
        (0xD64, 0xD65),
        (0xD80, 0xD80),
        (0xD84, 0xD84),
        (0xD97, 0xD99),
        (0xDB2, 0xDB2),
        (0xDBC, 0xDBC),
        (0xDBE, 0xDBF),
        (0xDC7, 0xDC9),
        (0xDCB, 0xDCE),
        (0xDD5, 0xDD5),
        (0xDD7, 0xDD7),
        (0xDE0, 0xDE5),
        (0xDF0, 0xDF1),
        (0xDF5, 0xE00),
        (0xE3B, 0xE3E),
        (0xE5C, 0xE80),
        (0xE83, 0xE83),
        (0xE85, 0xE85),
        (0xE8B, 0xE8B),
        (0xEA4, 0xEA4),
        (0xEA6, 0xEA6),
        (0xEBE, 0xEBF),
        (0xEC5, 0xEC5),
        (0xEC7, 0xEC7),
        (0xECF, 0xECF),
        (0xEDA, 0xEDB),
        (0xEE0, 0xEFF),
        (0xF48, 0xF48),
        (0xF6D, 0xF70),
        (0xF98, 0xF98),
        (0xFBD, 0xFBD),
        (0xFCD, 0xFCD),
        (0xFDB, 0xFFF),
        (0x10C6, 0x10C6),
        (0x10C8, 0x10CC),
        (0x10CE, 0x10CF),
        (0x1249, 0x1249),
        (0x124E, 0x124F),
        (0x1257, 0x1257),
        (0x1259, 0x1259),
        (0x125E, 0x125F),
        (0x1289, 0x1289),
        (0x128E, 0x128F),
        (0x12B1, 0x12B1),
        (0x12B6, 0x12B7),
        (0x12BF, 0x12BF),
        (0x12C1, 0x12C1),
        (0x12C6, 0x12C7),
        (0x12D7, 0x12D7),
        (0x1311, 0x1311),
        (0x1316, 0x1317),
        (0x135B, 0x135C),
        (0x137D, 0x137F),
        (0x139A, 0x139F),
        (0x13F6, 0x13F7),
        (0x13FE, 0x13FF),
        (0x169D, 0x169F),
        (0x16F9, 0x16FF),
        (0x1716, 0x171E),
        (0x1737, 0x173F),
        (0x1754, 0x175F),
        (0x176D, 0x176D),
        (0x1771, 0x1771),
        (0x1774, 0x177F),
        (0x17DE, 0x17DF),
        (0x17EA, 0x17EF),
        (0x17FA, 0x17FF),
        (0x181A, 0x181F),
        (0x1879, 0x187F),
        (0x18AB, 0x18AF),
        (0x18F6, 0x18FF),
        (0x191F, 0x191F),
        (0x192C, 0x192F),
        (0x193C, 0x193F),
        (0x1941, 0x1943),
        (0x196E, 0x196F),
        (0x1975, 0x197F),
        (0x19AC, 0x19AF),
        (0x19CA, 0x19CF),
        (0x19DB, 0x19DD),
        (0x1A1C, 0x1A1D),
        (0x1A5F, 0x1A5F),
        (0x1A7D, 0x1A7E),
        (0x1A8A, 0x1A8F),
        (0x1A9A, 0x1A9F),
        (0x1AAE, 0x1AAF),
        (0x1ADE, 0x1ADF),
        (0x1AEC, 0x1AFF),
        (0x1B4D, 0x1B4D),
        (0x1BF4, 0x1BFB),
        (0x1C38, 0x1C3A),
        (0x1C4A, 0x1C4C),
        (0x1C8B, 0x1C8F),
        (0x1CBB, 0x1CBC),
        (0x1CC8, 0x1CCF),
        (0x1CFB, 0x1CFF),
        (0x1F16, 0x1F17),
        (0x1F1E, 0x1F1F),
        (0x1F46, 0x1F47),
        (0x1F4E, 0x1F4F),
        (0x1F58, 0x1F58),
        (0x1F5A, 0x1F5A),
        (0x1F5C, 0x1F5C),
        (0x1F5E, 0x1F5E),
        (0x1F7E, 0x1F7F),
        (0x1FB5, 0x1FB5),
        (0x1FC5, 0x1FC5),
        (0x1FD4, 0x1FD5),
        (0x1FDC, 0x1FDC),
        (0x1FF0, 0x1FF1),
        (0x1FF5, 0x1FF5),
        (0x1FFF, 0x1FFF),
        (0x2065, 0x2065),
        (0x2072, 0x2073),
        (0x208F, 0x208F),
        (0x209D, 0x209F),
        (0x20C2, 0x20CF),
        (0x20F1, 0x20FF),
        (0x218C, 0x218F),
        (0x242A, 0x243F),
        (0x244B, 0x245F),
        (0x2B74, 0x2B75),
        (0x2CF4, 0x2CF8),
        (0x2D26, 0x2D26),
        (0x2D28, 0x2D2C),
        (0x2D2E, 0x2D2F),
        (0x2D68, 0x2D6E),
        (0x2D71, 0x2D7E),
        (0x2D97, 0x2D9F),
        (0x2DA7, 0x2DA7),
        (0x2DAF, 0x2DAF),
        (0x2DB7, 0x2DB7),
        (0x2DBF, 0x2DBF),
        (0x2DC7, 0x2DC7),
        (0x2DCF, 0x2DCF),
        (0x2DD7, 0x2DD7),
        (0x2DDF, 0x2DDF),
        (0x2E5E, 0x2E7F),
        (0x2E9A, 0x2E9A),
        (0x2EF4, 0x2EFF),
        (0x2FD6, 0x2FEF),
        (0x3040, 0x3040),
        (0x3097, 0x3098),
        (0x3100, 0x3104),
        (0x3130, 0x3130),
        (0x318F, 0x318F),
        (0x31E6, 0x31EE),
        (0x321F, 0x321F),
        (0xA48D, 0xA48F),
        (0xA4C7, 0xA4CF),
        (0xA62C, 0xA63F),
        (0xA6F8, 0xA6FF),
        (0xA7DD, 0xA7F0),
        (0xA82D, 0xA82F),
        (0xA83A, 0xA83F),
        (0xA878, 0xA87F),
        (0xA8C6, 0xA8CD),
        (0xA8DA, 0xA8DF),
        (0xA954, 0xA95E),
        (0xA97D, 0xA97F),
        (0xA9CE, 0xA9CE),
        (0xA9DA, 0xA9DD),
        (0xA9FF, 0xA9FF),
        (0xAA37, 0xAA3F),
        (0xAA4E, 0xAA4F),
        (0xAA5A, 0xAA5B),
        (0xAAC3, 0xAADA),
        (0xAAF7, 0xAB00),
        (0xAB07, 0xAB08),
        (0xAB0F, 0xAB10),
        (0xAB17, 0xAB1F),
        (0xAB27, 0xAB27),
        (0xAB2F, 0xAB2F),
        (0xAB6C, 0xAB6F),
        (0xABEE, 0xABEF),
        (0xABFA, 0xABFF),
        (0xD7A4, 0xD7AF),
        (0xD7C7, 0xD7CA),
        (0xD7FC, 0xF8FF),
        (0xFA6E, 0xFA6F),
        (0xFADA, 0xFAFF),
        (0xFB07, 0xFB12),
        (0xFB18, 0xFB1C),
        (0xFB37, 0xFB37),
        (0xFB3D, 0xFB3D),
        (0xFB3F, 0xFB3F),
        (0xFB42, 0xFB42),
        (0xFB45, 0xFB45),
        (0xFDD0, 0xFDEF),
        (0xFE1A, 0xFE1F),
        (0xFE53, 0xFE53),
        (0xFE67, 0xFE67),
        (0xFE6C, 0xFE6F),
        (0xFE75, 0xFE75),
        (0xFEFD, 0xFEFE),
        (0xFF00, 0xFF00),
        (0xFFBF, 0xFFC1),
        (0xFFC8, 0xFFC9),
        (0xFFD0, 0xFFD1),
        (0xFFD8, 0xFFD9),
        (0xFFDD, 0xFFDF),
        (0xFFE7, 0xFFE7),
        (0xFFEF, 0xFFF8),
        (0xFFFE, 0xFFFF),
        (0x1000C, 0x1000C),
        (0x10027, 0x10027),
        (0x1003B, 0x1003B),
        (0x1003E, 0x1003E),
        (0x1004E, 0x1004F),
        (0x1005E, 0x1007F),
        (0x100FB, 0x100FF),
        (0x10103, 0x10106),
        (0x10134, 0x10136),
        (0x1018F, 0x1018F),
        (0x1019D, 0x1019F),
        (0x101A1, 0x101CF),
        (0x101FE, 0x1027F),
        (0x1029D, 0x1029F),
        (0x102D1, 0x102DF),
        (0x102FC, 0x102FF),
        (0x10324, 0x1032C),
        (0x1034B, 0x1034F),
        (0x1037B, 0x1037F),
        (0x1039E, 0x1039E),
        (0x103C4, 0x103C7),
        (0x103D6, 0x103FF),
        (0x1049E, 0x1049F),
        (0x104AA, 0x104AF),
        (0x104D4, 0x104D7),
        (0x104FC, 0x104FF),
        (0x10528, 0x1052F),
        (0x10564, 0x1056E),
        (0x1057B, 0x1057B),
        (0x1058B, 0x1058B),
        (0x10593, 0x10593),
        (0x10596, 0x10596),
        (0x105A2, 0x105A2),
        (0x105B2, 0x105B2),
        (0x105BA, 0x105BA),
        (0x105BD, 0x105BF),
        (0x105F4, 0x105FF),
        (0x10737, 0x1073F),
        (0x10756, 0x1075F),
        (0x10768, 0x1077F),
        (0x10786, 0x10786),
        (0x107B1, 0x107B1),
        (0x107BB, 0x107FF),
        (0x10806, 0x10807),
        (0x10809, 0x10809),
        (0x10836, 0x10836),
        (0x10839, 0x1083B),
        (0x1083D, 0x1083E),
        (0x10856, 0x10856),
        (0x1089F, 0x108A6),
        (0x108B0, 0x108DF),
        (0x108F3, 0x108F3),
        (0x108F6, 0x108FA),
        (0x1091C, 0x1091E),
        (0x1093A, 0x1093E),
        (0x1095A, 0x1097F),
        (0x109B8, 0x109BB),
        (0x109D0, 0x109D1),
        (0x10A04, 0x10A04),
        (0x10A07, 0x10A0B),
        (0x10A14, 0x10A14),
        (0x10A18, 0x10A18),
        (0x10A36, 0x10A37),
        (0x10A3B, 0x10A3E),
        (0x10A49, 0x10A4F),
        (0x10A59, 0x10A5F),
        (0x10AA0, 0x10ABF),
        (0x10AE7, 0x10AEA),
        (0x10AF7, 0x10AFF),
        (0x10B36, 0x10B38),
        (0x10B56, 0x10B57),
        (0x10B73, 0x10B77),
        (0x10B92, 0x10B98),
        (0x10B9D, 0x10BA8),
        (0x10BB0, 0x10BFF),
        (0x10C49, 0x10C7F),
        (0x10CB3, 0x10CBF),
        (0x10CF3, 0x10CF9),
        (0x10D28, 0x10D2F),
        (0x10D3A, 0x10D3F),
        (0x10D66, 0x10D68),
        (0x10D86, 0x10D8D),
        (0x10D90, 0x10E5F),
        (0x10E7F, 0x10E7F),
        (0x10EAA, 0x10EAA),
        (0x10EAE, 0x10EAF),
        (0x10EB2, 0x10EC1),
        (0x10EC8, 0x10ECF),
        (0x10ED9, 0x10EF9),
        (0x10F28, 0x10F2F),
        (0x10F5A, 0x10F6F),
        (0x10F8A, 0x10FAF),
        (0x10FCC, 0x10FDF),
        (0x10FF7, 0x10FFF),
        (0x1104E, 0x11051),
        (0x11076, 0x1107E),
        (0x110C3, 0x110CC),
        (0x110CE, 0x110CF),
        (0x110E9, 0x110EF),
        (0x110FA, 0x110FF),
        (0x11135, 0x11135),
        (0x11148, 0x1114F),
        (0x11177, 0x1117F),
        (0x111E0, 0x111E0),
        (0x111F5, 0x111FF),
        (0x11212, 0x11212),
        (0x11242, 0x1127F),
        (0x11287, 0x11287),
        (0x11289, 0x11289),
        (0x1128E, 0x1128E),
        (0x1129E, 0x1129E),
        (0x112AA, 0x112AF),
        (0x112EB, 0x112EF),
        (0x112FA, 0x112FF),
        (0x11304, 0x11304),
        (0x1130D, 0x1130E),
        (0x11311, 0x11312),
        (0x11329, 0x11329),
        (0x11331, 0x11331),
        (0x11334, 0x11334),
        (0x1133A, 0x1133A),
        (0x11345, 0x11346),
        (0x11349, 0x1134A),
        (0x1134E, 0x1134F),
        (0x11351, 0x11356),
        (0x11358, 0x1135C),
        (0x11364, 0x11365),
        (0x1136D, 0x1136F),
        (0x11375, 0x1137F),
        (0x1138A, 0x1138A),
        (0x1138C, 0x1138D),
        (0x1138F, 0x1138F),
        (0x113B6, 0x113B6),
        (0x113C1, 0x113C1),
        (0x113C3, 0x113C4),
        (0x113C6, 0x113C6),
        (0x113CB, 0x113CB),
        (0x113D6, 0x113D6),
        (0x113D9, 0x113E0),
        (0x113E3, 0x113FF),
        (0x1145C, 0x1145C),
        (0x11462, 0x1147F),
        (0x114C8, 0x114CF),
        (0x114DA, 0x1157F),
        (0x115B6, 0x115B7),
        (0x115DE, 0x115FF),
        (0x11645, 0x1164F),
        (0x1165A, 0x1165F),
        (0x1166D, 0x1167F),
        (0x116BA, 0x116BF),
        (0x116CA, 0x116CF),
        (0x116E4, 0x116FF),
        (0x1171B, 0x1171C),
        (0x1172C, 0x1172F),
        (0x11747, 0x117FF),
        (0x1183C, 0x1189F),
        (0x118F3, 0x118FE),
        (0x11907, 0x11908),
        (0x1190A, 0x1190B),
        (0x11914, 0x11914),
        (0x11917, 0x11917),
        (0x11936, 0x11936),
        (0x11939, 0x1193A),
        (0x11947, 0x1194F),
        (0x1195A, 0x1199F),
        (0x119A8, 0x119A9),
        (0x119D8, 0x119D9),
        (0x119E5, 0x119FF),
        (0x11A48, 0x11A4F),
        (0x11AA3, 0x11AAF),
        (0x11AF9, 0x11AFF),
        (0x11B0A, 0x11B5F),
        (0x11B68, 0x11BBF),
        (0x11BE2, 0x11BEF),
        (0x11BFA, 0x11BFF),
        (0x11C09, 0x11C09),
        (0x11C37, 0x11C37),
        (0x11C46, 0x11C4F),
        (0x11C6D, 0x11C6F),
        (0x11C90, 0x11C91),
        (0x11CA8, 0x11CA8),
        (0x11CB7, 0x11CFF),
        (0x11D07, 0x11D07),
        (0x11D0A, 0x11D0A),
        (0x11D37, 0x11D39),
        (0x11D3B, 0x11D3B),
        (0x11D3E, 0x11D3E),
        (0x11D48, 0x11D4F),
        (0x11D5A, 0x11D5F),
        (0x11D66, 0x11D66),
        (0x11D69, 0x11D69),
        (0x11D8F, 0x11D8F),
        (0x11D92, 0x11D92),
        (0x11D99, 0x11D9F),
        (0x11DAA, 0x11DAF),
        (0x11DDC, 0x11DDF),
        (0x11DEA, 0x11EDF),
        (0x11EF9, 0x11EFF),
        (0x11F11, 0x11F11),
        (0x11F3B, 0x11F3D),
        (0x11F5B, 0x11FAF),
        (0x11FB1, 0x11FBF),
        (0x11FF2, 0x11FFE),
        (0x1239A, 0x123FF),
        (0x1246F, 0x1246F),
        (0x12475, 0x1247F),
        (0x12544, 0x12F8F),
        (0x12FF3, 0x12FFF),
        (0x13456, 0x1345F),
        (0x143FB, 0x143FF),
        (0x14647, 0x160FF),
        (0x1613A, 0x167FF),
        (0x16A39, 0x16A3F),
        (0x16A5F, 0x16A5F),
        (0x16A6A, 0x16A6D),
        (0x16ABF, 0x16ABF),
        (0x16ACA, 0x16ACF),
        (0x16AEE, 0x16AEF),
        (0x16AF6, 0x16AFF),
        (0x16B46, 0x16B4F),
        (0x16B5A, 0x16B5A),
        (0x16B62, 0x16B62),
        (0x16B78, 0x16B7C),
        (0x16B90, 0x16D3F),
        (0x16D7A, 0x16E3F),
        (0x16E9B, 0x16E9F),
        (0x16EB9, 0x16EBA),
        (0x16ED4, 0x16EFF),
        (0x16F4B, 0x16F4E),
        (0x16F88, 0x16F8E),
        (0x16FA0, 0x16FDF),
        (0x16FE5, 0x16FEF),
        (0x16FF7, 0x16FFF),
        (0x18CD6, 0x18CFE),
        (0x18D1F, 0x18D7F),
        (0x18DF3, 0x1AFEF),
        (0x1AFF4, 0x1AFF4),
        (0x1AFFC, 0x1AFFC),
        (0x1AFFF, 0x1AFFF),
        (0x1B123, 0x1B131),
        (0x1B133, 0x1B14F),
        (0x1B153, 0x1B154),
        (0x1B156, 0x1B163),
        (0x1B168, 0x1B16F),
        (0x1B2FC, 0x1BBFF),
        (0x1BC6B, 0x1BC6F),
        (0x1BC7D, 0x1BC7F),
        (0x1BC89, 0x1BC8F),
        (0x1BC9A, 0x1BC9B),
        (0x1BCA4, 0x1CBFF),
        (0x1CCFD, 0x1CCFF),
        (0x1CEB4, 0x1CEB9),
        (0x1CED1, 0x1CEDF),
        (0x1CEF1, 0x1CEFF),
        (0x1CF2E, 0x1CF2F),
        (0x1CF47, 0x1CF4F),
        (0x1CFC4, 0x1CFFF),
        (0x1D0F6, 0x1D0FF),
        (0x1D127, 0x1D128),
        (0x1D1EB, 0x1D1FF),
        (0x1D246, 0x1D2BF),
        (0x1D2D4, 0x1D2DF),
        (0x1D2F4, 0x1D2FF),
        (0x1D357, 0x1D35F),
        (0x1D379, 0x1D3FF),
        (0x1D455, 0x1D455),
        (0x1D49D, 0x1D49D),
        (0x1D4A0, 0x1D4A1),
        (0x1D4A3, 0x1D4A4),
        (0x1D4A7, 0x1D4A8),
        (0x1D4AD, 0x1D4AD),
        (0x1D4BA, 0x1D4BA),
        (0x1D4BC, 0x1D4BC),
        (0x1D4C4, 0x1D4C4),
        (0x1D506, 0x1D506),
        (0x1D50B, 0x1D50C),
        (0x1D515, 0x1D515),
        (0x1D51D, 0x1D51D),
        (0x1D53A, 0x1D53A),
        (0x1D53F, 0x1D53F),
        (0x1D545, 0x1D545),
        (0x1D547, 0x1D549),
        (0x1D551, 0x1D551),
        (0x1D6A6, 0x1D6A7),
        (0x1D7CC, 0x1D7CD),
        (0x1DA8C, 0x1DA9A),
        (0x1DAA0, 0x1DAA0),
        (0x1DAB0, 0x1DEFF),
        (0x1DF1F, 0x1DF24),
        (0x1DF2B, 0x1DFFF),
        (0x1E007, 0x1E007),
        (0x1E019, 0x1E01A),
        (0x1E022, 0x1E022),
        (0x1E025, 0x1E025),
        (0x1E02B, 0x1E02F),
        (0x1E06E, 0x1E08E),
        (0x1E090, 0x1E0FF),
        (0x1E12D, 0x1E12F),
        (0x1E13E, 0x1E13F),
        (0x1E14A, 0x1E14D),
        (0x1E150, 0x1E28F),
        (0x1E2AF, 0x1E2BF),
        (0x1E2FA, 0x1E2FE),
        (0x1E300, 0x1E4CF),
        (0x1E4FA, 0x1E5CF),
        (0x1E5FB, 0x1E5FE),
        (0x1E600, 0x1E6BF),
        (0x1E6DF, 0x1E6DF),
        (0x1E6F6, 0x1E6FD),
        (0x1E700, 0x1E7DF),
        (0x1E7E7, 0x1E7E7),
        (0x1E7EC, 0x1E7EC),
        (0x1E7EF, 0x1E7EF),
        (0x1E7FF, 0x1E7FF),
        (0x1E8C5, 0x1E8C6),
        (0x1E8D7, 0x1E8FF),
        (0x1E94C, 0x1E94F),
        (0x1E95A, 0x1E95D),
        (0x1E960, 0x1EC70),
        (0x1ECB5, 0x1ED00),
        (0x1ED3E, 0x1EDFF),
        (0x1EE04, 0x1EE04),
        (0x1EE20, 0x1EE20),
        (0x1EE23, 0x1EE23),
        (0x1EE25, 0x1EE26),
        (0x1EE28, 0x1EE28),
        (0x1EE33, 0x1EE33),
        (0x1EE38, 0x1EE38),
        (0x1EE3A, 0x1EE3A),
        (0x1EE3C, 0x1EE41),
        (0x1EE43, 0x1EE46),
        (0x1EE48, 0x1EE48),
        (0x1EE4A, 0x1EE4A),
        (0x1EE4C, 0x1EE4C),
        (0x1EE50, 0x1EE50),
        (0x1EE53, 0x1EE53),
        (0x1EE55, 0x1EE56),
        (0x1EE58, 0x1EE58),
        (0x1EE5A, 0x1EE5A),
        (0x1EE5C, 0x1EE5C),
        (0x1EE5E, 0x1EE5E),
        (0x1EE60, 0x1EE60),
        (0x1EE63, 0x1EE63),
        (0x1EE65, 0x1EE66),
        (0x1EE6B, 0x1EE6B),
        (0x1EE73, 0x1EE73),
        (0x1EE78, 0x1EE78),
        (0x1EE7D, 0x1EE7D),
        (0x1EE7F, 0x1EE7F),
        (0x1EE8A, 0x1EE8A),
        (0x1EE9C, 0x1EEA0),
        (0x1EEA4, 0x1EEA4),
        (0x1EEAA, 0x1EEAA),
        (0x1EEBC, 0x1EEEF),
        (0x1EEF2, 0x1EFFF),
        (0x1F02C, 0x1F02F),
        (0x1F094, 0x1F09F),
        (0x1F0AF, 0x1F0B0),
        (0x1F0C0, 0x1F0C0),
        (0x1F0D0, 0x1F0D0),
        (0x1F0F6, 0x1F0FF),
        (0x1F1AE, 0x1F1E5),
        (0x1F203, 0x1F20F),
        (0x1F23C, 0x1F23F),
        (0x1F249, 0x1F24F),
        (0x1F252, 0x1F25F),
        (0x1F266, 0x1F2FF),
        (0x1F6D9, 0x1F6DB),
        (0x1F6ED, 0x1F6EF),
        (0x1F6FD, 0x1F6FF),
        (0x1F7DA, 0x1F7DF),
        (0x1F7EC, 0x1F7EF),
        (0x1F7F1, 0x1F7FF),
        (0x1F80C, 0x1F80F),
        (0x1F848, 0x1F84F),
        (0x1F85A, 0x1F85F),
        (0x1F888, 0x1F88F),
        (0x1F8AE, 0x1F8AF),
        (0x1F8BC, 0x1F8BF),
        (0x1F8C2, 0x1F8CF),
        (0x1F8D9, 0x1F8FF),
        (0x1FA58, 0x1FA5F),
        (0x1FA6E, 0x1FA6F),
        (0x1FA7D, 0x1FA7F),
        (0x1FA8B, 0x1FA8D),
        (0x1FAC7, 0x1FAC7),
        (0x1FAC9, 0x1FACC),
        (0x1FADD, 0x1FADE),
        (0x1FAEB, 0x1FAEE),
        (0x1FAF9, 0x1FAFF),
        (0x1FB93, 0x1FB93),
        (0x1FBFB, 0x1FFFF),
        (0x2A6E0, 0x2A6FF),
        (0x2B81E, 0x2B81F),
        (0x2CEAE, 0x2CEAF),
        (0x2EBE1, 0x2EBEF),
        (0x2EE5E, 0x2F7FF),
        (0x2FA1E, 0x2FFFF),
        (0x3134B, 0x3134F),
        (0x3347A, 0xE0000),
        (0xE0002, 0xE001F),
        (0xE0080, 0xE00FF),
        (0xE01F0, 0x10FFFF)
    ];

    private static readonly (int Start, int End)[] VaiRanges =
    [
        (0xA500, 0xA62B)
    ];

    private static readonly (int Start, int End)[] VithkuqiRanges =
    [
        (0x10570, 0x1057A),
        (0x1057C, 0x1058A),
        (0x1058C, 0x10592),
        (0x10594, 0x10595),
        (0x10597, 0x105A1),
        (0x105A3, 0x105B1),
        (0x105B3, 0x105B9),
        (0x105BB, 0x105BC)
    ];

    private static readonly (int Start, int End)[] WanchoRanges =
    [
        (0x1E2C0, 0x1E2F9),
        (0x1E2FF, 0x1E2FF)
    ];

    private static readonly (int Start, int End)[] WarangCitiRanges =
    [
        (0x118A0, 0x118F2),
        (0x118FF, 0x118FF)
    ];

    private static readonly (int Start, int End)[] YezidiRanges =
    [
        (0x10E80, 0x10EA9),
        (0x10EAB, 0x10EAD),
        (0x10EB0, 0x10EB1)
    ];

    private static readonly (int Start, int End)[] YiRanges =
    [
        (0xA000, 0xA48C),
        (0xA490, 0xA4C6)
    ];

    private static readonly (int Start, int End)[] ZanabazarSquareRanges =
    [
        (0x11A00, 0x11A47)
    ];

    public static bool IsSupported(string script)
    {
        return script switch
        {
            "Adlam" or "Adlm" => true,
            "Ahom" => true,
            "Anatolian_Hieroglyphs" or "Hluw" => true,
            "Arabic" or "Arab" => true,
            "Armenian" or "Armn" => true,
            "Avestan" or "Avst" => true,
            "Balinese" or "Bali" => true,
            "Bamum" or "Bamu" => true,
            "Bassa_Vah" or "Bass" => true,
            "Batak" or "Batk" => true,
            "Bengali" or "Beng" => true,
            "Beria_Erfe" or "Berf" => true,
            "Bhaiksuki" or "Bhks" => true,
            "Bopomofo" or "Bopo" => true,
            "Brahmi" or "Brah" => true,
            "Braille" or "Brai" => true,
            "Buginese" or "Bugi" => true,
            "Buhid" or "Buhd" => true,
            "Canadian_Aboriginal" or "Cans" => true,
            "Carian" or "Cari" => true,
            "Caucasian_Albanian" or "Aghb" => true,
            "Chakma" or "Cakm" => true,
            "Cham" => true,
            "Cherokee" or "Cher" => true,
            "Chorasmian" or "Chrs" => true,
            "Common" or "Zyyy" => true,
            "Coptic" or "Copt" or "Qaac" => true,
            "Cuneiform" or "Xsux" => true,
            "Cypriot" or "Cprt" => true,
            "Cypro_Minoan" or "Cpmn" => true,
            "Cyrillic" or "Cyrl" => true,
            "Deseret" or "Dsrt" => true,
            "Devanagari" or "Deva" => true,
            "Dives_Akuru" or "Diak" => true,
            "Dogra" or "Dogr" => true,
            "Duployan" or "Dupl" => true,
            "Egyptian_Hieroglyphs" or "Egyp" => true,
            "Elbasan" or "Elba" => true,
            "Elymaic" or "Elym" => true,
            "Ethiopic" or "Ethi" => true,
            "Garay" or "Gara" => true,
            "Georgian" or "Geor" => true,
            "Glagolitic" or "Glag" => true,
            "Gothic" or "Goth" => true,
            "Grantha" or "Gran" => true,
            "Greek" or "Grek" => true,
            "Gujarati" or "Gujr" => true,
            "Gunjala_Gondi" or "Gong" => true,
            "Gurmukhi" or "Guru" => true,
            "Gurung_Khema" or "Gukh" => true,
            "Han" or "Hani" => true,
            "Hangul" or "Hang" => true,
            "Hanifi_Rohingya" or "Rohg" => true,
            "Hanunoo" or "Hano" => true,
            "Hatran" or "Hatr" => true,
            "Hebrew" or "Hebr" => true,
            "Hiragana" or "Hira" => true,
            "Imperial_Aramaic" or "Armi" => true,
            "Inherited" or "Zinh" or "Qaai" => true,
            "Inscriptional_Pahlavi" or "Phli" => true,
            "Inscriptional_Parthian" or "Prti" => true,
            "Javanese" or "Java" => true,
            "Kaithi" or "Kthi" => true,
            "Kannada" or "Knda" => true,
            "Katakana" or "Kana" => true,
            "Kawi" => true,
            "Kayah_Li" or "Kali" => true,
            "Kharoshthi" or "Khar" => true,
            "Khitan_Small_Script" or "Kits" => true,
            "Khmer" or "Khmr" => true,
            "Khojki" or "Khoj" => true,
            "Khudawadi" or "Sind" => true,
            "Kirat_Rai" or "Krai" => true,
            "Lao" or "Laoo" => true,
            "Latin" or "Latn" => true,
            "Lepcha" or "Lepc" => true,
            "Limbu" or "Limb" => true,
            "Linear_A" or "Lina" => true,
            "Linear_B" or "Linb" => true,
            "Lisu" => true,
            "Lycian" or "Lyci" => true,
            "Lydian" or "Lydi" => true,
            "Mahajani" or "Mahj" => true,
            "Makasar" or "Maka" => true,
            "Malayalam" or "Mlym" => true,
            "Mandaic" or "Mand" => true,
            "Manichaean" or "Mani" => true,
            "Marchen" or "Marc" => true,
            "Masaram_Gondi" or "Gonm" => true,
            "Medefaidrin" or "Medf" => true,
            "Meetei_Mayek" or "Mtei" => true,
            "Mende_Kikakui" or "Mend" => true,
            "Meroitic_Cursive" or "Merc" => true,
            "Meroitic_Hieroglyphs" or "Mero" => true,
            "Miao" or "Plrd" => true,
            "Modi" => true,
            "Mongolian" or "Mong" => true,
            "Mro" or "Mroo" => true,
            "Multani" or "Mult" => true,
            "Myanmar" or "Mymr" => true,
            "Nabataean" or "Nbat" => true,
            "Nag_Mundari" or "Nagm" => true,
            "Nandinagari" or "Nand" => true,
            "New_Tai_Lue" or "Talu" => true,
            "Newa" => true,
            "Nko" or "Nkoo" => true,
            "Nushu" or "Nshu" => true,
            "Nyiakeng_Puachue_Hmong" or "Hmnp" => true,
            "Ogham" or "Ogam" => true,
            "Ol_Chiki" or "Olck" => true,
            "Ol_Onal" or "Onao" => true,
            "Old_Hungarian" or "Hung" => true,
            "Old_Italic" or "Ital" => true,
            "Old_North_Arabian" or "Narb" => true,
            "Old_Permic" or "Perm" => true,
            "Old_Persian" or "Xpeo" => true,
            "Old_Sogdian" or "Sogo" => true,
            "Old_South_Arabian" or "Sarb" => true,
            "Old_Turkic" or "Orkh" => true,
            "Old_Uyghur" or "Ougr" => true,
            "Oriya" or "Orya" => true,
            "Osage" or "Osge" => true,
            "Osmanya" or "Osma" => true,
            "Pahawh_Hmong" or "Hmng" => true,
            "Palmyrene" or "Palm" => true,
            "Pau_Cin_Hau" or "Pauc" => true,
            "Phags_Pa" or "Phag" => true,
            "Phoenician" or "Phnx" => true,
            "Psalter_Pahlavi" or "Phlp" => true,
            "Rejang" or "Rjng" => true,
            "Runic" or "Runr" => true,
            "Samaritan" or "Samr" => true,
            "Saurashtra" or "Saur" => true,
            "Sharada" or "Shrd" => true,
            "Shavian" or "Shaw" => true,
            "Siddham" or "Sidd" => true,
            "Sidetic" or "Sidt" => true,
            "SignWriting" or "Sgnw" => true,
            "Sinhala" or "Sinh" => true,
            "Sogdian" or "Sogd" => true,
            "Sora_Sompeng" or "Sora" => true,
            "Soyombo" or "Soyo" => true,
            "Sundanese" or "Sund" => true,
            "Sunuwar" or "Sunu" => true,
            "Syloti_Nagri" or "Sylo" => true,
            "Syriac" or "Syrc" => true,
            "Tagalog" or "Tglg" => true,
            "Tagbanwa" or "Tagb" => true,
            "Tai_Le" or "Tale" => true,
            "Tai_Tham" or "Lana" => true,
            "Tai_Viet" or "Tavt" => true,
            "Tai_Yo" or "Tayo" => true,
            "Takri" or "Takr" => true,
            "Tamil" or "Taml" => true,
            "Tangsa" or "Tnsa" => true,
            "Tangut" or "Tang" => true,
            "Telugu" or "Telu" => true,
            "Thaana" or "Thaa" => true,
            "Thai" => true,
            "Tibetan" or "Tibt" => true,
            "Tifinagh" or "Tfng" => true,
            "Tirhuta" or "Tirh" => true,
            "Todhri" or "Todr" => true,
            "Tolong_Siki" or "Tols" => true,
            "Toto" => true,
            "Tulu_Tigalari" or "Tutg" => true,
            "Ugaritic" or "Ugar" => true,
            "Unknown" or "Zzzz" => true,
            "Vai" or "Vaii" => true,
            "Vithkuqi" or "Vith" => true,
            "Wancho" or "Wcho" => true,
            "Warang_Citi" or "Wara" => true,
            "Yezidi" or "Yezi" => true,
            "Yi" or "Yiii" => true,
            "Zanabazar_Square" or "Zanb" => true,
            _ => false
        };
    }

    public static bool Contains(string script, int codePoint)
    {
        return script switch
        {
            "Adlam" or "Adlm" => ContainsRange(AdlamRanges, codePoint),
            "Ahom" => ContainsRange(AhomRanges, codePoint),
            "Anatolian_Hieroglyphs" or "Hluw" => ContainsRange(AnatolianHieroglyphsRanges, codePoint),
            "Arabic" or "Arab" => ContainsRange(ArabicRanges, codePoint),
            "Armenian" or "Armn" => ContainsRange(ArmenianRanges, codePoint),
            "Avestan" or "Avst" => ContainsRange(AvestanRanges, codePoint),
            "Balinese" or "Bali" => ContainsRange(BalineseRanges, codePoint),
            "Bamum" or "Bamu" => ContainsRange(BamumRanges, codePoint),
            "Bassa_Vah" or "Bass" => ContainsRange(BassaVahRanges, codePoint),
            "Batak" or "Batk" => ContainsRange(BatakRanges, codePoint),
            "Bengali" or "Beng" => ContainsRange(BengaliRanges, codePoint),
            "Beria_Erfe" or "Berf" => ContainsRange(BeriaErfeRanges, codePoint),
            "Bhaiksuki" or "Bhks" => ContainsRange(BhaiksukiRanges, codePoint),
            "Bopomofo" or "Bopo" => ContainsRange(BopomofoRanges, codePoint),
            "Brahmi" or "Brah" => ContainsRange(BrahmiRanges, codePoint),
            "Braille" or "Brai" => ContainsRange(BrailleRanges, codePoint),
            "Buginese" or "Bugi" => ContainsRange(BugineseRanges, codePoint),
            "Buhid" or "Buhd" => ContainsRange(BuhidRanges, codePoint),
            "Canadian_Aboriginal" or "Cans" => ContainsRange(CanadianAboriginalRanges, codePoint),
            "Carian" or "Cari" => ContainsRange(CarianRanges, codePoint),
            "Caucasian_Albanian" or "Aghb" => ContainsRange(CaucasianAlbanianRanges, codePoint),
            "Chakma" or "Cakm" => ContainsRange(ChakmaRanges, codePoint),
            "Cham" => ContainsRange(ChamRanges, codePoint),
            "Cherokee" or "Cher" => ContainsRange(CherokeeRanges, codePoint),
            "Chorasmian" or "Chrs" => ContainsRange(ChorasmianRanges, codePoint),
            "Common" or "Zyyy" => ContainsRange(CommonRanges, codePoint),
            "Coptic" or "Copt" or "Qaac" => ContainsRange(CopticRanges, codePoint),
            "Cuneiform" or "Xsux" => ContainsRange(CuneiformRanges, codePoint),
            "Cypriot" or "Cprt" => ContainsRange(CypriotRanges, codePoint),
            "Cypro_Minoan" or "Cpmn" => ContainsRange(CyproMinoanRanges, codePoint),
            "Cyrillic" or "Cyrl" => ContainsRange(CyrillicRanges, codePoint),
            "Deseret" or "Dsrt" => ContainsRange(DeseretRanges, codePoint),
            "Devanagari" or "Deva" => ContainsRange(DevanagariRanges, codePoint),
            "Dives_Akuru" or "Diak" => ContainsRange(DivesAkuruRanges, codePoint),
            "Dogra" or "Dogr" => ContainsRange(DograRanges, codePoint),
            "Duployan" or "Dupl" => ContainsRange(DuployanRanges, codePoint),
            "Egyptian_Hieroglyphs" or "Egyp" => ContainsRange(EgyptianHieroglyphsRanges, codePoint),
            "Elbasan" or "Elba" => ContainsRange(ElbasanRanges, codePoint),
            "Elymaic" or "Elym" => ContainsRange(ElymaicRanges, codePoint),
            "Ethiopic" or "Ethi" => ContainsRange(EthiopicRanges, codePoint),
            "Garay" or "Gara" => ContainsRange(GarayRanges, codePoint),
            "Georgian" or "Geor" => ContainsRange(GeorgianRanges, codePoint),
            "Glagolitic" or "Glag" => ContainsRange(GlagoliticRanges, codePoint),
            "Gothic" or "Goth" => ContainsRange(GothicRanges, codePoint),
            "Grantha" or "Gran" => ContainsRange(GranthaRanges, codePoint),
            "Greek" or "Grek" => ContainsRange(GreekRanges, codePoint),
            "Gujarati" or "Gujr" => ContainsRange(GujaratiRanges, codePoint),
            "Gunjala_Gondi" or "Gong" => ContainsRange(GunjalaGondiRanges, codePoint),
            "Gurmukhi" or "Guru" => ContainsRange(GurmukhiRanges, codePoint),
            "Gurung_Khema" or "Gukh" => ContainsRange(GurungKhemaRanges, codePoint),
            "Han" or "Hani" => ContainsRange(HanRanges, codePoint),
            "Hangul" or "Hang" => ContainsRange(HangulRanges, codePoint),
            "Hanifi_Rohingya" or "Rohg" => ContainsRange(HanifiRohingyaRanges, codePoint),
            "Hanunoo" or "Hano" => ContainsRange(HanunooRanges, codePoint),
            "Hatran" or "Hatr" => ContainsRange(HatranRanges, codePoint),
            "Hebrew" or "Hebr" => ContainsRange(HebrewRanges, codePoint),
            "Hiragana" or "Hira" => ContainsRange(HiraganaRanges, codePoint),
            "Imperial_Aramaic" or "Armi" => ContainsRange(ImperialAramaicRanges, codePoint),
            "Inherited" or "Zinh" or "Qaai" => ContainsRange(InheritedRanges, codePoint),
            "Inscriptional_Pahlavi" or "Phli" => ContainsRange(InscriptionalPahlaviRanges, codePoint),
            "Inscriptional_Parthian" or "Prti" => ContainsRange(InscriptionalParthianRanges, codePoint),
            "Javanese" or "Java" => ContainsRange(JavaneseRanges, codePoint),
            "Kaithi" or "Kthi" => ContainsRange(KaithiRanges, codePoint),
            "Kannada" or "Knda" => ContainsRange(KannadaRanges, codePoint),
            "Katakana" or "Kana" => ContainsRange(KatakanaRanges, codePoint),
            "Kawi" => ContainsRange(KawiRanges, codePoint),
            "Kayah_Li" or "Kali" => ContainsRange(KayahLiRanges, codePoint),
            "Kharoshthi" or "Khar" => ContainsRange(KharoshthiRanges, codePoint),
            "Khitan_Small_Script" or "Kits" => ContainsRange(KhitanSmallScriptRanges, codePoint),
            "Khmer" or "Khmr" => ContainsRange(KhmerRanges, codePoint),
            "Khojki" or "Khoj" => ContainsRange(KhojkiRanges, codePoint),
            "Khudawadi" or "Sind" => ContainsRange(KhudawadiRanges, codePoint),
            "Kirat_Rai" or "Krai" => ContainsRange(KiratRaiRanges, codePoint),
            "Lao" or "Laoo" => ContainsRange(LaoRanges, codePoint),
            "Latin" or "Latn" => ContainsRange(LatinRanges, codePoint),
            "Lepcha" or "Lepc" => ContainsRange(LepchaRanges, codePoint),
            "Limbu" or "Limb" => ContainsRange(LimbuRanges, codePoint),
            "Linear_A" or "Lina" => ContainsRange(LinearARanges, codePoint),
            "Linear_B" or "Linb" => ContainsRange(LinearBRanges, codePoint),
            "Lisu" => ContainsRange(LisuRanges, codePoint),
            "Lycian" or "Lyci" => ContainsRange(LycianRanges, codePoint),
            "Lydian" or "Lydi" => ContainsRange(LydianRanges, codePoint),
            "Mahajani" or "Mahj" => ContainsRange(MahajaniRanges, codePoint),
            "Makasar" or "Maka" => ContainsRange(MakasarRanges, codePoint),
            "Malayalam" or "Mlym" => ContainsRange(MalayalamRanges, codePoint),
            "Mandaic" or "Mand" => ContainsRange(MandaicRanges, codePoint),
            "Manichaean" or "Mani" => ContainsRange(ManichaeanRanges, codePoint),
            "Marchen" or "Marc" => ContainsRange(MarchenRanges, codePoint),
            "Masaram_Gondi" or "Gonm" => ContainsRange(MasaramGondiRanges, codePoint),
            "Medefaidrin" or "Medf" => ContainsRange(MedefaidrinRanges, codePoint),
            "Meetei_Mayek" or "Mtei" => ContainsRange(MeeteiMayekRanges, codePoint),
            "Mende_Kikakui" or "Mend" => ContainsRange(MendeKikakuiRanges, codePoint),
            "Meroitic_Cursive" or "Merc" => ContainsRange(MeroiticCursiveRanges, codePoint),
            "Meroitic_Hieroglyphs" or "Mero" => ContainsRange(MeroiticHieroglyphsRanges, codePoint),
            "Miao" or "Plrd" => ContainsRange(MiaoRanges, codePoint),
            "Modi" => ContainsRange(ModiRanges, codePoint),
            "Mongolian" or "Mong" => ContainsRange(MongolianRanges, codePoint),
            "Mro" or "Mroo" => ContainsRange(MroRanges, codePoint),
            "Multani" or "Mult" => ContainsRange(MultaniRanges, codePoint),
            "Myanmar" or "Mymr" => ContainsRange(MyanmarRanges, codePoint),
            "Nabataean" or "Nbat" => ContainsRange(NabataeanRanges, codePoint),
            "Nag_Mundari" or "Nagm" => ContainsRange(NagMundariRanges, codePoint),
            "Nandinagari" or "Nand" => ContainsRange(NandinagariRanges, codePoint),
            "New_Tai_Lue" or "Talu" => ContainsRange(NewTaiLueRanges, codePoint),
            "Newa" => ContainsRange(NewaRanges, codePoint),
            "Nko" or "Nkoo" => ContainsRange(NkoRanges, codePoint),
            "Nushu" or "Nshu" => ContainsRange(NushuRanges, codePoint),
            "Nyiakeng_Puachue_Hmong" or "Hmnp" => ContainsRange(NyiakengPuachueHmongRanges, codePoint),
            "Ogham" or "Ogam" => ContainsRange(OghamRanges, codePoint),
            "Ol_Chiki" or "Olck" => ContainsRange(OlChikiRanges, codePoint),
            "Ol_Onal" or "Onao" => ContainsRange(OlOnalRanges, codePoint),
            "Old_Hungarian" or "Hung" => ContainsRange(OldHungarianRanges, codePoint),
            "Old_Italic" or "Ital" => ContainsRange(OldItalicRanges, codePoint),
            "Old_North_Arabian" or "Narb" => ContainsRange(OldNorthArabianRanges, codePoint),
            "Old_Permic" or "Perm" => ContainsRange(OldPermicRanges, codePoint),
            "Old_Persian" or "Xpeo" => ContainsRange(OldPersianRanges, codePoint),
            "Old_Sogdian" or "Sogo" => ContainsRange(OldSogdianRanges, codePoint),
            "Old_South_Arabian" or "Sarb" => ContainsRange(OldSouthArabianRanges, codePoint),
            "Old_Turkic" or "Orkh" => ContainsRange(OldTurkicRanges, codePoint),
            "Old_Uyghur" or "Ougr" => ContainsRange(OldUyghurRanges, codePoint),
            "Oriya" or "Orya" => ContainsRange(OriyaRanges, codePoint),
            "Osage" or "Osge" => ContainsRange(OsageRanges, codePoint),
            "Osmanya" or "Osma" => ContainsRange(OsmanyaRanges, codePoint),
            "Pahawh_Hmong" or "Hmng" => ContainsRange(PahawhHmongRanges, codePoint),
            "Palmyrene" or "Palm" => ContainsRange(PalmyreneRanges, codePoint),
            "Pau_Cin_Hau" or "Pauc" => ContainsRange(PauCinHauRanges, codePoint),
            "Phags_Pa" or "Phag" => ContainsRange(PhagsPaRanges, codePoint),
            "Phoenician" or "Phnx" => ContainsRange(PhoenicianRanges, codePoint),
            "Psalter_Pahlavi" or "Phlp" => ContainsRange(PsalterPahlaviRanges, codePoint),
            "Rejang" or "Rjng" => ContainsRange(RejangRanges, codePoint),
            "Runic" or "Runr" => ContainsRange(RunicRanges, codePoint),
            "Samaritan" or "Samr" => ContainsRange(SamaritanRanges, codePoint),
            "Saurashtra" or "Saur" => ContainsRange(SaurashtraRanges, codePoint),
            "Sharada" or "Shrd" => ContainsRange(SharadaRanges, codePoint),
            "Shavian" or "Shaw" => ContainsRange(ShavianRanges, codePoint),
            "Siddham" or "Sidd" => ContainsRange(SiddhamRanges, codePoint),
            "Sidetic" or "Sidt" => ContainsRange(SideticRanges, codePoint),
            "SignWriting" or "Sgnw" => ContainsRange(SignWritingRanges, codePoint),
            "Sinhala" or "Sinh" => ContainsRange(SinhalaRanges, codePoint),
            "Sogdian" or "Sogd" => ContainsRange(SogdianRanges, codePoint),
            "Sora_Sompeng" or "Sora" => ContainsRange(SoraSompengRanges, codePoint),
            "Soyombo" or "Soyo" => ContainsRange(SoyomboRanges, codePoint),
            "Sundanese" or "Sund" => ContainsRange(SundaneseRanges, codePoint),
            "Sunuwar" or "Sunu" => ContainsRange(SunuwarRanges, codePoint),
            "Syloti_Nagri" or "Sylo" => ContainsRange(SylotiNagriRanges, codePoint),
            "Syriac" or "Syrc" => ContainsRange(SyriacRanges, codePoint),
            "Tagalog" or "Tglg" => ContainsRange(TagalogRanges, codePoint),
            "Tagbanwa" or "Tagb" => ContainsRange(TagbanwaRanges, codePoint),
            "Tai_Le" or "Tale" => ContainsRange(TaiLeRanges, codePoint),
            "Tai_Tham" or "Lana" => ContainsRange(TaiThamRanges, codePoint),
            "Tai_Viet" or "Tavt" => ContainsRange(TaiVietRanges, codePoint),
            "Tai_Yo" or "Tayo" => ContainsRange(TaiYoRanges, codePoint),
            "Takri" or "Takr" => ContainsRange(TakriRanges, codePoint),
            "Tamil" or "Taml" => ContainsRange(TamilRanges, codePoint),
            "Tangsa" or "Tnsa" => ContainsRange(TangsaRanges, codePoint),
            "Tangut" or "Tang" => ContainsRange(TangutRanges, codePoint),
            "Telugu" or "Telu" => ContainsRange(TeluguRanges, codePoint),
            "Thaana" or "Thaa" => ContainsRange(ThaanaRanges, codePoint),
            "Thai" => ContainsRange(ThaiRanges, codePoint),
            "Tibetan" or "Tibt" => ContainsRange(TibetanRanges, codePoint),
            "Tifinagh" or "Tfng" => ContainsRange(TifinaghRanges, codePoint),
            "Tirhuta" or "Tirh" => ContainsRange(TirhutaRanges, codePoint),
            "Todhri" or "Todr" => ContainsRange(TodhriRanges, codePoint),
            "Tolong_Siki" or "Tols" => ContainsRange(TolongSikiRanges, codePoint),
            "Toto" => ContainsRange(TotoRanges, codePoint),
            "Tulu_Tigalari" or "Tutg" => ContainsRange(TuluTigalariRanges, codePoint),
            "Ugaritic" or "Ugar" => ContainsRange(UgariticRanges, codePoint),
            "Unknown" or "Zzzz" => ContainsRange(UnknownRanges, codePoint),
            "Vai" or "Vaii" => ContainsRange(VaiRanges, codePoint),
            "Vithkuqi" or "Vith" => ContainsRange(VithkuqiRanges, codePoint),
            "Wancho" or "Wcho" => ContainsRange(WanchoRanges, codePoint),
            "Warang_Citi" or "Wara" => ContainsRange(WarangCitiRanges, codePoint),
            "Yezidi" or "Yezi" => ContainsRange(YezidiRanges, codePoint),
            "Yi" or "Yiii" => ContainsRange(YiRanges, codePoint),
            "Zanabazar_Square" or "Zanb" => ContainsRange(ZanabazarSquareRanges, codePoint),
            _ => false
        };
    }

    private static bool ContainsRange((int Start, int End)[] ranges, int codePoint)
    {
        var lo = 0;
        var hi = ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            var (start, end) = ranges[mid];
            if (codePoint < start)
                hi = mid - 1;
            else if (codePoint > end)
                lo = mid + 1;
            else
                return true;
        }

        return false;
    }
}
