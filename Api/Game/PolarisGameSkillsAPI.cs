using System;
using System.Collections.Generic;
using nel;

namespace Polaris
{
    public static partial class PolarisAPI
    {
        public static partial class Game
        {
            /// <summary>
            /// 技能目录的查询入口。单个技能的读写在 <see cref="API.GameSkill"/> 实例上。
            /// </summary>
            public static class Skills
            {
                /// <summary>按稳定键名取得技能实例；当前游戏版本没有该技能时返回 <c>null</c>。</summary>
                public static API.GameSkill Resolve(string skillKey)
                {
                    if (string.IsNullOrEmpty(skillKey))
                    {
                        return null;
                    }

                    try
                    {
                        return API.GameSkill.Wrap(SkillManager.Get(skillKey));
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

                /// <summary>
                /// 取得当前游戏版本定义的全部技能，保持原版目录的枚举顺序。
                /// 返回只读快照；目录还没初始化时返回空列表而不是 <c>null</c>。
                /// </summary>
                public static IReadOnlyList<API.GameSkill> GetAll()
                {
                    try
                    {
                        // var 而非具名类型：目录容器 NDic<> 住在原版自己的命名空间里，
                        // 这一层没必要为了一个局部变量把那个命名空间引进来。
                        var definitions = SkillManager.getSkillDictionary();
                        if (definitions == null)
                        {
                            return Array.Empty<API.GameSkill>();
                        }

                        var result = new List<API.GameSkill>();
                        foreach (KeyValuePair<string, PrSkill> entry in definitions)
                        {
                            API.GameSkill wrapper = API.GameSkill.Wrap(entry.Value);
                            if (wrapper != null)
                            {
                                result.Add(wrapper);
                            }
                        }

                        return result;
                    }
                    catch (Exception)
                    {
                        return Array.Empty<API.GameSkill>();
                    }
                }
            }
        }
    }
}
