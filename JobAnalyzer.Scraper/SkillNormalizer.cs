namespace JobAnalyzer.Scraper
{
    /// <summary>
    /// AI'ın farklı biçimlerde yazdığı skill adlarını canonical forma dönüştürür.
    /// Örn: "c#", "C# / .NET", "csharp" → "C#"
    /// </summary>
    public static class SkillNormalizer
    {
        // Alias → canonical form
        private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            // C# / .NET
            { "c#", "C#" }, { "csharp", "C#" }, { "c sharp", "C#" },
            { ".net", ".NET" }, { "dotnet", ".NET" }, { "dot net", ".NET" },
            { "asp.net", "ASP.NET" }, { "asp.net core", "ASP.NET Core" }, { "asp.net mvc", "ASP.NET MVC" },
            { ".net core", ".NET Core" }, { ".net framework", ".NET Framework" },
            { "entity framework", "Entity Framework" }, { "ef core", "Entity Framework" },

            // JavaScript / Frontend
            { "javascript", "JavaScript" }, { "js", "JavaScript" }, { "ecmascript", "JavaScript" },
            { "typescript", "TypeScript" }, { "ts", "TypeScript" },
            { "react", "React" }, { "react.js", "React" }, { "reactjs", "React" },
            { "vue", "Vue.js" }, { "vue.js", "Vue.js" }, { "vuejs", "Vue.js" },
            { "angular", "Angular" }, { "angularjs", "Angular" },
            { "next.js", "Next.js" }, { "nextjs", "Next.js" },
            { "nuxt", "Nuxt.js" }, { "nuxt.js", "Nuxt.js" },
            { "html", "HTML" }, { "html5", "HTML" },
            { "css", "CSS" }, { "css3", "CSS" },
            { "tailwind", "Tailwind CSS" }, { "tailwindcss", "Tailwind CSS" },
            { "bootstrap", "Bootstrap" },

            // Node
            { "node", "Node.js" }, { "node.js", "Node.js" }, { "nodejs", "Node.js" },
            { "express", "Express.js" }, { "express.js", "Express.js" },

            // Python
            { "python", "Python" }, { "python3", "Python" },
            { "django", "Django" }, { "flask", "Flask" },
            { "fastapi", "FastAPI" },

            // Java
            { "java", "Java" },
            { "spring", "Spring" }, { "spring boot", "Spring Boot" }, { "springboot", "Spring Boot" },

            // Database
            { "sql", "SQL" }, { "sql server", "SQL Server" }, { "mssql", "SQL Server" }, { "t-sql", "SQL Server" },
            { "mysql", "MySQL" }, { "postgresql", "PostgreSQL" }, { "postgres", "PostgreSQL" },
            { "mongodb", "MongoDB" }, { "mongo", "MongoDB" },
            { "redis", "Redis" }, { "elasticsearch", "Elasticsearch" },
            { "sqlite", "SQLite" }, { "oracle", "Oracle" }, { "nosql", "NoSQL" },

            // Cloud / DevOps
            { "aws", "AWS" }, { "amazon web services", "AWS" },
            { "azure", "Azure" }, { "microsoft azure", "Azure" },
            { "gcp", "GCP" }, { "google cloud", "GCP" },
            { "docker", "Docker" }, { "kubernetes", "Kubernetes" }, { "k8s", "Kubernetes" },
            { "ci/cd", "CI/CD" }, { "cicd", "CI/CD" }, { "jenkins", "Jenkins" },
            { "github actions", "GitHub Actions" }, { "gitlab ci", "GitLab CI" },
            { "linux", "Linux" }, { "bash", "Bash" }, { "terraform", "Terraform" },
            { "ansible", "Ansible" },

            // Mobile
            { "flutter", "Flutter" }, { "dart", "Dart" },
            { "react native", "React Native" }, { "reactnative", "React Native" },
            { "swift", "Swift" }, { "kotlin", "Kotlin" }, { "android", "Android" }, { "ios", "iOS" },

            // AI/ML
            { "machine learning", "Machine Learning" }, { "ml", "Machine Learning" },
            { "deep learning", "Deep Learning" },
            { "tensorflow", "TensorFlow" }, { "pytorch", "PyTorch" },
            { "langchain", "LangChain" }, { "openai", "OpenAI" },
            { "nlp", "NLP" }, { "llm", "LLM" },

            // Diğer
            { "git", "Git" }, { "github", "Git" }, { "gitlab", "GitLab" },
            { "rest", "REST API" }, { "rest api", "REST API" }, { "restful", "REST API" },
            { "graphql", "GraphQL" },
            { "microservices", "Microservices" }, { "micro services", "Microservices" },
            { "agile", "Agile" }, { "scrum", "Scrum" },
            { "go", "Go" }, { "golang", "Go" },
            { "rust", "Rust" }, { "php", "PHP" }, { "ruby", "Ruby" }, { "scala", "Scala" },
            { "r", "R" }, { "matlab", "MATLAB" },
        };

        public static string Normalize(string rawSkills)
        {
            if (string.IsNullOrWhiteSpace(rawSkills)) return rawSkills;

            var parts = rawSkills.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Select(s => _aliases.TryGetValue(s, out var canonical) ? canonical : ToTitleCase(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return string.Join(", ", parts);
        }

        public static string NormalizeSingle(string skill)
        {
            skill = skill.Trim();
            return _aliases.TryGetValue(skill, out var canonical) ? canonical : ToTitleCase(skill);
        }

        private static string ToTitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Tamamen büyük harf olan kısaltmalar için dokunma (SQL, AWS vb.)
            if (s.Length <= 4 && s == s.ToUpper()) return s;
            return char.ToUpper(s[0]) + s[1..];
        }
    }
}
