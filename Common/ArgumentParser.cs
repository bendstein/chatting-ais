﻿namespace ChattingAIs.Common
{
    public class ArgumentParser(
        List<string>? positional_parameter_names = null,
        HashSet<string>? allowed_flags = null,
        Dictionary<char, string>? abbreviated_parameter_names = null)
    {
        private const string
            PARAM_NAME_PFX = "--",
            ABBREVIATED_NAME_PFX = "-";

        private List<string> positional_parameter_names = positional_parameter_names ?? [];

        private HashSet<string> allowed_flags = allowed_flags ?? [];

        private Dictionary<char, string> abbreviated_parameter_names = abbreviated_parameter_names ?? [];

        public ArgumentParser WithPositionalParameters(params string[] positional_parameter_names)
        {
            this.positional_parameter_names = [.. positional_parameter_names];
            return this;
        }

        public ArgumentParser WithAllowedFlags(params string[] flags)
        {
            this.allowed_flags = [.. flags];
            return this;
        }

        public ArgumentParser WithAbbreviations(params (char abbreviated_name, string full_name)[] abbreviations)
        {
            abbreviated_parameter_names = abbreviations.ToDictionary(
                pair => char.ToUpperInvariant(pair.abbreviated_name),
                pair => pair.full_name.ToUpperInvariant());

            return this;
        }

        public ArgumentParser AddAllowedFlags(params string[] flags)
        {
            foreach (var flag in flags)
                this.allowed_flags.Add(flag);

            return this;
        }

        public ArgumentParser RemoveAllowedFlags(params string[] flags)
        {
            foreach (var flag in flags)
                this.allowed_flags.Remove(flag);

            return this;
        }

        public ArgumentParser AddAbbreviation(params (char abbreviated_name, string full_name)[] abbreviations)
        {
            foreach (var (abbreviated_name, full_name) in abbreviations)
                abbreviated_parameter_names[char.ToUpperInvariant(abbreviated_name)] = full_name.ToUpperInvariant();

            return this;
        }

        public ArgumentParser RemoveAbbreviation(params char[] abbreviations)
        {
            foreach (var abbreviated_name in abbreviations)
                abbreviated_parameter_names.Remove(abbreviated_name);

            return this;
        }

        public Dictionary<string, string?> ParseArguments(params string[] args)
        {
            Dictionary<string, string?> arguments = [];

            //Normalize abbreviated params
            var normalized_abbreviations = abbreviated_parameter_names.ToDictionary(
                pair => char.ToUpperInvariant(pair.Key),
                pair => pair.Value
                );

            //Normalize allowed flags
            var normalized_flags = allowed_flags.Select(f => f.ToUpperInvariant()).ToHashSet();

            //Until a named param is seen, position args are allowed
            bool allow_positional = true;
            int position = 0;

            //Iterate over arguments
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].Trim().ToUpperInvariant();

                bool consume_next = false;
                string? param_name = null;
                string? param_value = null;

                //If this is an abbreviated parameter, change to its full name
                if (arg.StartsWith(ABBREVIATED_NAME_PFX) && arg.Length == ABBREVIATED_NAME_PFX.Length + 1
                    && normalized_abbreviations.TryGetValue(arg[ABBREVIATED_NAME_PFX.Length..][0], out var full_param_name))
                {
                    //A named parameter is seen. Positional arguments are no longer allowed.
                    allow_positional = false;

                    param_name = full_param_name;

                    //Mark that next arg should be consumed for value
                    consume_next = true;
                }
                //Named parameter
                else if (arg.StartsWith(PARAM_NAME_PFX) && arg.Length > PARAM_NAME_PFX.Length)
                {
                    //A named parameter is seen. Positional arguments are no longer allowed.
                    allow_positional = false;

                    //Remove prefix from arg name
                    param_name = arg[PARAM_NAME_PFX.Length..];

                    //Mark that next arg should be consumed for value
                    consume_next = true;
                }
                //Positional parameter
                else if (allow_positional && position < positional_parameter_names.Count)
                {
                    //Get param name for position
                    param_name = positional_parameter_names[position];

                    //Set param value
                    param_value = args[i];

                    //Increment positional param index
                    position++;
                }
                //Invalid parameter
                else
                {
                    throw new ArgumentException($"Unexpected argument '{args[i]}'. (Argument {i})");
                }

                //Normalize param name
                param_name = param_name.ToUpperInvariant().Trim();

                //Get value from next arg
                if (consume_next && args.Length > i + 1)
                {
                    var next_arg = args[i + 1].Trim();

                    //If next argument is a parameter name, skip
                    if (next_arg.StartsWith(ABBREVIATED_NAME_PFX) && next_arg.Length == ABBREVIATED_NAME_PFX.Length + 1
                        && normalized_abbreviations.TryGetValue(next_arg[ABBREVIATED_NAME_PFX.Length..].ToUpperInvariant()[0], out _))
                    {
                        //Abbreviated param name
                    }
                    else if (next_arg.StartsWith(PARAM_NAME_PFX) && next_arg.Length > PARAM_NAME_PFX.Length + 1)
                    {
                        //Param name
                    }
                    else
                    {
                        //Set param value and increment
                        param_value = args[++i];
                    }
                }

                if (param_value is null)
                {
                    //If no value, and param name is allowed to be a flag,
                    //assign value to "true"
                    if (normalized_flags.Contains(param_name))
                    {
                        arguments.Add(param_name, true.ToString());
                    }
                    //If no value, and param name is NOT allowed to be a flag,
                    //assign value to null
                    else
                    {
                        arguments.Add(param_name, null);
                    }
                }
                //If value is present, assign to param name
                else
                {
                    arguments.Add(param_name, param_value);
                }
            }

            return arguments;
        }
    }
}