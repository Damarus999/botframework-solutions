﻿namespace NewsSkill.Models
{
    public class NewsSkillState
    {
        public NewsSkillState()
        {
        }

        public Luis.NewsLuis LuisResult { get; set; }

        public string CurrentCoordinates { get; set; }
    }
}
