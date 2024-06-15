using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
#pragma warning disable CS1591

namespace ConfusedPolarBear.Plugin.IntroSkipper.Db
{
    public enum SegmentType
    {
        Intro,
        Credits
    }

    public class Segment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]

        [Required]
        public required Guid EpisodeId { get; set; }

        [Required]
        public required double Start { get; set; }

        [Required]
        public required double End { get; set; }

        [Required]
        public required SegmentType Type { get; set; }
    }
}
