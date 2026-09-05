namespace BabyDance
{
    /// <summary>
    /// ログ行の接頭辞。tools/unity.sh がこの文字列を grep して完了マーカーと異常を拾うため、
    /// ランタイムと Editor の全アセンブリでここ 1 箇所の定義を参照する。
    /// </summary>
    public static class Log
    {
        public const string Tag = "[BabyDance]";
    }
}
