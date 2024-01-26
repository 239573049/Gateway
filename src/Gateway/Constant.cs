﻿namespace Gateway;

public static class Constant
{
    private const string Prefix = "Gateway";

    public sealed class Setting
    {
        private const string Default = Prefix + ":Setting";

        /// <summary>
        /// 最大请求Body的大小（MB）
        /// </summary>
        public const string MaxRequestBodySize = Default + ":MaxRequestBodySize";
    }
}