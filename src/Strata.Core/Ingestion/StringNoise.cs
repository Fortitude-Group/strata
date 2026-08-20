using System;

namespace Strata.Core.Ingestion;

/// <summary>
/// Recognises ELF/toolchain **metadata** strings that appear in essentially every binary — ELF section
/// names, symbol-version tags (GLIBC_/GCC_/CXXABI_/GLIBCXX_), and build-id/interpreter noise. These are
/// not library identity, so filtering them out of both corpus and scan strings stops every library from
/// "matching" every target's boilerplate (the dominant cross-library false-positive source).
/// </summary>
public static class StringNoise
{
    public static bool IsMetadata(string s)
    {
        if (s.Length == 0)
        {
            return true;
        }

        // Symbol-version tags: GLIBC_2.3, GCC_4.2.0, CXXABI_1.3, GLIBCXX_3.4 ...
        if (s.StartsWith("GLIBC_", StringComparison.Ordinal) || s.StartsWith("GLIBCXX_", StringComparison.Ordinal)
            || s.StartsWith("GCC_", StringComparison.Ordinal) || s.StartsWith("CXXABI_", StringComparison.Ordinal))
        {
            return true;
        }

        // ELF section names: a dot followed by lowercase/dot/underscore/digits (.text, .eh_frame,
        // .note.gnu.build-id, .gnu.version_r, .rela.dyn ...). Real library strings almost never take this form.
        if (s[0] == '.' && s.Length >= 2)
        {
            bool sectionShaped = true;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsLower(c) || char.IsDigit(c) || c == '.' || c == '_' || c == '-'))
                {
                    sectionShaped = false;
                    break;
                }
            }

            if (sectionShaped)
            {
                return true;
            }
        }

        // Interpreter / dynamic-loader and common ABI-tag noise.
        return s is "GNU" or "/lib64/ld-linux-x86-64.so.2" or "/lib/ld-linux-aarch64.so.1"
            or "__gmon_start__" or "_ITM_deregisterTMCloneTable" or "_ITM_registerTMCloneTable"
            or "__cxa_finalize" or "GLIBC_ABI_DT_RELR";
    }
}
