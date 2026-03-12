using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Text;
using TFTStats.Core.Entities;

namespace TFTStats.Core.Infrastructure
{
    public class TFTStatsDbContext: DbContext
    {
        public DbSet<Player> Player => Set<Player>();
        public DbSet<Match> Match => Set<Match>();
        public DbSet<Participant> Participant => Set<Participant>();
        public DbSet<ParticipantUnit> ParticipantUnits => Set<ParticipantUnit>();
        public DbSet<ParticipantUnit> ParticipantTraits => Set<ParticipantUnit>();


        public TFTStatsDbContext(DbContextOptions<TFTStatsDbContext> options) 
            :base(options) { }

    }
}
