using System;
using System.Collections.Generic;
using System.Linq;
using ConfusedPolarBear.Plugin.IntroSkipper.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
#pragma warning disable CS1591

namespace ConfusedPolarBear.Plugin.IntroSkipper
{
    public class SegmentService
    {
        private readonly SegmentContext _context;
        private readonly ILogger<SegmentService> _logger;

        public SegmentService(SegmentContext context, ILogger<SegmentService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public void AddSegment(Segment segment)
        {
            try
            {
                _context.Segments.Add(segment);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding segment to database");
            }
        }

        public void RemoveSegment(Segment segment)
        {
            try
            {
                _context.Segments.Remove(segment);
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing segment from database");
            }
        }

        public List<Segment> GetSegments(SegmentType type)
        {
            try
            {
                return [.. _context.Segments.Where(s => s.Type == type)];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving segments from database");
                return [];
            }
        }

        public void MigrateDatabase()
        {
            try
            {
                _context.Database.Migrate();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating database");
            }
        }
    }
}
