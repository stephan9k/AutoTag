﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;


namespace autotag.Core {
    public class MovieProcessor : IProcessor {
        private TMDbClient tmdb;

        public MovieProcessor(string apiKey) {
            this.tmdb = new TMDbClient(apiKey);
        }

        public async Task<bool> process(
            string filePath,
            Action<string> setPath,
            Action<string, MessageType> setStatus,
            Func<List<Tuple<string, string>>, int> selectResult,
            AutoTagConfig config
        ) {
            FileMetadata result = new FileMetadata(FileMetadata.Types.Movie);

            #region "Filename parsing"
            string pattern =
                "^((?<Title>.+?)[\\. _-]?)" + // get title by reading from start to a field (whichever field comes first)
                "?(" +
                    "([\\(]?(?<Year>(19|20)[0-9]{2})[\\)]?)|" + // year - extract for use in searching
                    "([0-9]{3,4}(p|i))|" + // resolution (e.g. 1080p, 720i)
                    "((?:PPV\\.)?[HPS]DTV|[. ](?:HD)?CAM[| ]|B[DR]Rip|[.| ](?:HD-?)?TS[.| ]|(?:PPV )?WEB-?DL(?: DVDRip)?|HDRip|DVDRip|CamRip|W[EB]Rip|BluRay|DvDScr|hdtv|REMUX|3D|Half-(OU|SBS)+|4K|NF|AMZN)|" + // rip type
                    "(xvid|[hx]\\.?26[45]|AVC)|" + // video codec
                    "(MP3|DD5\\.?1|Dual[\\- ]Audio|LiNE|DTS[-HD]+|AAC[.-]LC|AAC(?:\\.?2\\.0)?|AC3(?:\\.5\\.1)?|7\\.1|DDP5.1)|" + // audio codec
                    "(REPACK|INTERNAL|PROPER)|" + // scene tags
                    "\\.(mp4|m4v|mkv)$" + // file extensions
                ")";

            Match match = Regex.Match(Path.GetFileName(filePath), pattern);
            string title, year;
            if (match.Success) {
                title = match.Groups["Title"].ToString();
                year = match.Groups["Year"].ToString();
            } else {
                setStatus("Error: Failed to parse required information from filename", MessageType.Error);
                return false;
            }

            title = title.Replace('.', ' '); // change dots to spaces

            if (String.IsNullOrWhiteSpace(title)) {
                setStatus("Error: Failed to parse required information from filename", MessageType.Error);
                return false;
            }

            setStatus($"Parsed file as {title}", MessageType.Information);
            #endregion

            #region "TMDB API Searching"
            SearchContainer<SearchMovie> searchResults;
            if (!String.IsNullOrWhiteSpace(year)) {
                searchResults = await tmdb.SearchMovieAsync(query: title, year: int.Parse(year)); // if year was parsed, use it to narrow down search further
            } else {
                searchResults = await tmdb.SearchMovieAsync(query: title);
            }

            int selected = 0;

            if (searchResults.Results.Count > 1 && (searchResults.Results[0].Title != title || config.manualMode)) {
                selected = selectResult(
                    searchResults.Results
                        .Select(m => new Tuple<string, string>(
                            m.Title,
                            m.ReleaseDate == null ? "Unknown" : m.ReleaseDate.Value.Year.ToString()
                        )).ToList()
                );
            } else if (searchResults.Results.Count == 0) {
                setStatus($"Error: failed to find title {title} on TheMovieDB", MessageType.Error);
                result.Success = false;
                return false;
            }

            SearchMovie selectedResult = searchResults.Results[selected];

            setStatus($"Found {selectedResult.Title} ({selectedResult.ReleaseDate.Value.Year}) on TheMovieDB", MessageType.Information);
            #endregion

            result.Title = selectedResult.Title;
            result.Overview = selectedResult.Overview;
            result.CoverURL = (String.IsNullOrEmpty(selectedResult.PosterPath)) ? null : $"https://image.tmdb.org/t/p/original{selectedResult.PosterPath}";
            result.CoverFilename = selectedResult.PosterPath.Replace("/", "");
            result.Date = selectedResult.ReleaseDate.Value;

            if (String.IsNullOrEmpty(result.CoverURL)) {
                setStatus("Error: failed to fetch movie cover", MessageType.Error);
                result.Complete = false;
            }

            bool taggingSuccess = FileWriter.write(filePath, result, setPath, setStatus, config);

            return taggingSuccess && result.Success && result.Complete;
        }
    }
}
